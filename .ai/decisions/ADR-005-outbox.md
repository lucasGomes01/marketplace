# ADR-005 — Outbox transacional para publicar eventos

- **Data:** 2026-10-20
- **Status:** aceita (T-027)

## Contexto
Cada serviço gravava no próprio banco e publicava no Kafka como **dois passos independentes** (*dual write*). Não
existe transação que abranja o SQL Server e o Kafka, então sempre havia uma janela em que um passo acontecia e o
outro não:

| Onde | Ordem | Falha possível |
|---|---|---|
| Cart, `POST /cart/checkout` | publica → limpa o carrinho | Kafka fora → **503**; publicou e a gravação falhou → pedido criado com o carrinho cheio |
| Orders, `PUT /shipments/...` e "Recebi" | grava → publica | publicação falhou → **evento perdido** (nada reentrega uma requisição HTTP) |
| Orders e Catalog, ao consumir | grava → publica | salvo pela reentrega do Kafka, mas o consumidor fica preso ao broker para publicar |

O caso do envio dependia de "repita a requisição" para republicar, ou seja, de uma pessoa perceber o problema.

## Decisão
**Transactional outbox**, com relay por polling, em todos os serviços que publicam (Cart, Catalog, Orders):

1. O evento é gravado na tabela `OutboxMessages` **do banco do próprio serviço**, na mesma transação da mudança de
   negócio: `Sequence` (identity, ordem), `EventId` (único), `Type`, `Key`, `Payload` (envelope JSON pronto),
   `TraceParent`, `CreatedAt`, `PublishedAt` (null = pendente), `Attempts`, `LastError`.
2. Um **relay** em segundo plano, no próprio serviço, lê até 100 pendentes por `Sequence`, **dispara o lote inteiro
   em ordem e espera os acks juntos** (o produtor idempotente mantém a ordem por partição com vários envios em voo) e
   marca `PublishedAt` do trecho antes do primeiro erro num UPDATE só, pela lista de sequências. O que falhou e o que
   veio depois ficam pendentes e voltam no próximo lote, em ordem, com backoff (0,5 s → 10 s).
   A primeira versão esperava um ack por vez: no teste de carga a fila do Cart chegou a 31 eventos (~2 s); em lote,
   ficou em ≤ 4 (≤ 0,26 s).
3. **Um relay ativo por serviço**: `sp_getapplock` (modo `Session`) numa conexão dedicada, **fora do pool**, porque
   com pool fechar a conexão não encerra a sessão e o lock ficaria preso. Se a instância cair, a sessão fecha e outra
   cópia assume (tentativa a cada 5 s).
4. Latência: o relay acorda **logo depois do commit** (aviso em memória: `SavedChanges` no EF, callback no store Go) e,
   para eventos de outras cópias, por polling de 1 s.
5. O `traceparent` da requisição é gravado na linha e vira o pai do span de publicação: o trace da T-022 continua
   inteiro mesmo com o relay no meio.
6. Histórico publicado é apagado depois de 7 dias. Métricas `marketplace.outbox.pending` e
   `marketplace.outbox.oldest_age` alimentam o painel "Outbox".

Implementação: `BuildingBlocks.Outbox` (.NET: o `IEventPublisher` passa a só **registrar** o evento no DbContext) e
`internal/outbox` + `internal/store` (Go: o `Repository` recebe o `app.Event` junto com a mudança).

## Alternativas consideradas
- **CDC com Debezium** (ler o log de transações do SQL Server e publicar). É o que se usa em escala e não custa nada
  à aplicação, mas exige Kafka Connect, CDC ligado no SQL Server e mais um sistema para operar. Fica como evolução
  se o polling virar gargalo.
- **Vários relays com `READPAST`** (cada um pega um lote diferente): mais vazão, mas dois eventos do mesmo pedido
  podem sair fora de ordem (`Packed` antes de `Picked`). A ordem por chave vale mais aqui.
- **Publicar e compensar** (publica; se a gravação falhar, publica um evento de desfazer): mais código em cada caso de
  uso e o consumidor vê um estado que nunca existiu.
- **"Listen to yourself"** (só publica e o próprio serviço consome para gravar): troca a consistência imediata da API
  por consistência eventual até para quem fez a requisição.

## Consequências
- (+) Nenhum evento se perde e nenhum sai sem a mudança gravada. O checkout funciona com o Kafka fora (o pedido
  fica `Pending` até o Kafka voltar).
- (+) Casos de uso mais simples: sem tratamento de falha de broker, sem "repita a requisição".
- (−) Latência extra entre o commit e o evento no Kafka (normalmente milissegundos; o painel mostra a idade da fila).
- (−) Uma tabela e um processo de fundo a mais por serviço; o banco passa a ser também uma fila.
- (−) Continua **at-least-once**: o relay reenvia se cair entre o ack e o `PublishedAt`, e reenvia o que já tinha
  sido confirmado depois de uma falha no lote. Os consumidores seguem idempotentes (o *inbox* fica de fora: já há
  idempotência por `eventId`/estado).
- (−) Um relay ativo por serviço limita a vazão de publicação; com o envio em lote, aceitável no volume atual (ver a
  comparação de carga registrada na T-027).
- (−) Ordem entre transações concorrentes segue a ordem do `Sequence` (identity), não a do commit: uma transação lenta
  pode ser publicada depois de uma mais nova. Para o mesmo agregado as transações são sequenciais, então a ordem por
  pedido se mantém.
