# T-027 — Outbox: evento gravado na mesma transação do dado

- **Serviço:** Cart + Catalog (.NET) · Orders (Go) · Infra (painel)
- **Depende de:** T-026 (a branch parte da Fase 7, ainda não mergeada na `main`)
- **Status:** done (especificação aprovada em 2026-10-20 com as decisões recomendadas; relay em lote decidido
  durante a tarefa, depois do primeiro teste de carga)
- **Origem:** ideia I-01

## Objetivo
Acabar com o *dual write* (gravar no banco **e** publicar no Kafka como dois passos independentes): o evento passa
a ser gravado numa tabela `OutboxMessages` **na mesma transação** da mudança de negócio, e um *relay* em segundo plano
publica no Kafka depois. Resultado: nenhum evento se perde, e a API deixa de depender do Kafka estar no ar.

## O problema hoje (onde cada serviço publica)

| Evento | Quem publica | Ordem atual | O que pode dar errado |
|---|---|---|---|
| `cart.checked-out` | Cart, `POST /cart/checkout` (HTTP) | publica → limpa o carrinho | Kafka fora → **503 no checkout**. Publicou e a gravação falhou → pedido criado com o carrinho ainda cheio. |
| `stock.reserved` / `stock.rejected` | Catalog, ao consumir `order.placed` | grava a reserva → publica | Falha ao publicar: salva pela **reentrega** do `order.placed` (republica a reserva guardada). Funciona, mas o consumidor fica preso ao Kafka para publicar. |
| `order.placed` | Orders, ao consumir `cart.checked-out` | grava o pedido → publica | Igual ao Catalog: salvo pela reentrega. |
| `shipment.status-changed` | Orders, `PUT /shipments/...` e "Recebi" (HTTP) | grava o status → publica | **Evento perdido de verdade**: o status foi gravado, a publicação falhou e ninguém reentrega. Hoje a correção é "repita a requisição" (ver `events.md`). |

Os dois casos HTTP mostram o problema clássico. Os consumidores já são salvos pela reentrega do Kafka (at-least-once),
mas passam a ganhar a mesma garantia de forma uniforme.

## Como vai funcionar

```
  requisição / evento consumido
            │
            ▼
  ┌──────────── uma transação ────────────┐
  │  muda o dado (pedido, carrinho, ...)  │
  │  INSERT OutboxMessages (evento)       │
  └───────────────────────────────────────┘
            │ commit
            ▼
  relay (segundo plano, no próprio serviço)
     lê pendentes em ordem → publica no Kafka → marca PublishedAt
```

- **Tabela `OutboxMessages`** no banco de cada serviço que publica (CartDb, CatalogDb, OrdersDb — nenhum serviço lê o
  banco do outro): `Sequence` (bigint identity, ordem de gravação), `EventId` (único — duplicata é ignorada), `Type`
  (= tópico), `Key`, `Payload` (envelope JSON já pronto, `occurredAt` = hora da gravação), `TraceParent`,
  `CreatedAt`, `PublishedAt` (null = pendente), `Attempts`, `LastError`.
- **Relay**: lê um lote de pendentes por `Sequence`, dispara o lote inteiro em ordem e espera os acks juntos (versão
  final; a primeira esperava um ack por vez, ver "Carga"), marca `PublishedAt` do trecho antes do primeiro erro.
  Se uma publicação falhar, ela e as seguintes ficam pendentes (para não furar a ordem), registra `Attempts`/`LastError`
  e tenta de novo com backoff. Acorda a cada intervalo curto **e** logo depois de um commit que gravou evento (sinal em memória), para não
  somar a latência do polling ao fluxo de compra.
- **Uma instância de relay ativa por serviço**, garantida por um lock do SQL Server (`sp_getapplock`). Com várias cópias
  do serviço, as outras ficam de reserva e assumem se a ativa cair. Isso preserva a ordem por chave (ver decisão 2).
- **Trace contínuo**: o `traceparent` da requisição original é gravado na linha e usado na publicação, para que o trace
  da T-022 continue indo do gateway até a confirmação, mesmo com o relay no meio.
- **Limpeza**: linhas publicadas há mais de 7 dias são apagadas pelo próprio relay.
- **Métricas** (Prometheus, painel "Marketplace"): mensagens pendentes e idade da mais antiga, por serviço. É o sinal
  de "o Kafka está fora e o outbox está acumulando".

### .NET (Cart e Catalog) — novo `BuildingBlocks.Outbox`
- `OutboxEventPublisher : IEventPublisher` **não publica**: adiciona a linha ao `DbContext` do serviço. Ela é gravada no
  próximo `SaveChanges`, na mesma transação da mudança. A Application continua dependendo só de `IEventPublisher`.
- Configuração EF da entidade + migration em cada serviço; `OutboxRelay` como `BackgroundService` que usa o
  `KafkaEventPublisher` atual para publicar de fato.
- **Cart**: o checkout passa a ser "limpa o carrinho + registra o evento" numa só transação. Sem Kafka → **202**, e o pedido
  aparece como `Pending` quando o Kafka voltar. O 503 `events.unavailable` deixa de existir.
- **Catalog**: a reserva e o evento de resultado na mesma transação. Reentrega do `order.placed` com reserva já
  existente → não grava nada (o evento já está no outbox, publicado ou pendente).

### Go (Orders)
- `store` grava o evento na mesma `tx` do `Insert` (pedido) e do `UpdateShipment`. `app.Publisher` sai dos casos de uso,
  que passam a entregar os eventos ao `Repository` junto com a mudança.
- Relay como goroutine iniciada no `main`, usando o cliente franz-go atual. Some o "repetir a requisição republica".

## Decisões para revisar (minha recomendação primeiro)
1. **Relay por polling da tabela** (recomendado) × **CDC com Debezium** (lê o log de transações do SQL Server).
   Polling: zero infraestrutura nova, dá para entender e testar tudo. Debezium é o que se usa em escala, mas exige
   Kafka Connect + CDC ligado no SQL Server. Fica registrado na ADR como alternativa estudada.
2. **Ordem: um relay ativo por serviço via `sp_getapplock`** (recomendado) × vários relays com `READPAST` (mais
   vazão, mas dois eventos do mesmo pedido podem sair fora de ordem, por exemplo `Packed` antes de `Picked`).
3. **Escopo: os 3 serviços nesta tarefa** (recomendado, é o mesmo padrão nas duas linguagens) × só o Orders agora
   (onde o evento se perde de verdade) e o resto numa T-028.
4. **ADR-005 — Outbox** registrando o porquê, as alternativas (CDC, publicar-e-compensar, "listen to yourself") e as
   consequências (latência extra, tabela a mais, consumidores continuam idempotentes).

## Critérios de aceite (cada um vira ao menos um teste)
- [x] .NET: `OutboxEventPublisher` registra a mensagem com envelope, chave, `eventId` e `traceparent`, sem chamar o
      Kafka (teste de integração com SQL real, não unitário: a convenção proíbe mockar o DbContext).
- [x] .NET (integração, SQL real): falha no `SaveChanges` → nem o dado nem a mensagem ficam gravados; o mesmo
      `eventId` registrado duas vezes vira uma linha só.
- [x] .NET (integração, SQL + Kafka reais): relay publica os pendentes em ordem e marca `PublishedAt`; um commit acorda
      o relay sem esperar o polling. Kafka fora → mensagem continua pendente e `Attempts` sobe. "Publicada quando o
      broker volta" = parada no erro + nova tentativa, coberta no unitário (`OutboxProcessorTests`) e no Go com o store real.
- [x] .NET: com duas instâncias, só uma pega o lock; liberado, a outra assume.
- [x] Cart (integração): checkout com Kafka fora → 202, carrinho vazio, mensagem pendente no outbox.
- [x] Catalog (unit + integração): evento registrado antes de salvar (mesma transação); reentrega do `order.placed`
      não reserva nem registra de novo (uma mensagem no tópico, uma linha no outbox).
- [x] Orders (unit + store real): pedido e `order.placed` na mesma `tx`; mudança de envio e `shipment.status-changed`
      na mesma `tx`; conflito (409) e checkout repetido não gravam evento; `traceparent` gravado com o evento.
- [x] Orders (integração): relay com o store real publica em ordem e, depois de uma falha, tenta de novo sem deixar os
      seguintes passarem na frente; lock exclusivo; transporte Kafka envia o envelope gravado com chave e headers.
- [x] Métricas de pendentes e idade nos 3 serviços + painel "Outbox" no `build-dashboard.mjs`.
- [x] E2E continua verde (compra → confirmado → entregue), contra o compose com outbox.
- [x] Carga: `compra` antes (`VERSAO=antes-outbox`) e depois (`VERSAO=outbox`, `outbox-2`); resultado abaixo.
- [x] Trace contínuo conferido no Tempo: gateway → Cart → `cart.checked-out publish` (relay) → Orders →
      `order.placed publish` (relay) → Catalog → `stock.reserved publish` (relay) → Orders.

### Carga: `compra` (50 compradores, ~2 min, mesma máquina, 2026-10-20)

| Versão | Checkout mediana | Checkout p95 | Até confirmar (mediana) | Até confirmar p95 | Fila máx. no outbox (Cart) |
|---|---|---|---|---|---|
| `antes-outbox` | 15 ms | 143 ms | 506 ms | 3,5 s | — |
| `outbox` | 8 ms | 132 ms | 505 ms | 6,1 s | 8 eventos / 2,0 s |
| `outbox-2` | 10 ms | 165 ms | 511 ms | 8,3 s | 31 eventos / 1,9 s |
| `outbox-lote` | 8 ms | 22 ms | 504 ms | 0,52 s | 2 eventos / 0,01 s |
| `outbox-lote-2` | 9 ms | 140 ms | 1,0 s | 4,5 s | 4 eventos / 0,26 s |

- **Checkout mais rápido** (mediana cai à metade): a requisição não espera mais o ack do Kafka.
- **Primeira versão do relay (um evento por vez, esperando o ack)**: cauda do "até confirmar" pior. Sob 50
  compradores o relay do Cart acumulava fila (até 31 pendentes, ~2 s no painel "Outbox").
- **Relay em lote (decidido com o dono durante a tarefa)**: dispara o lote inteiro em ordem, espera os acks juntos e
  marca o trecho publicado num UPDATE só. A fila do outbox caiu para ≤ 4 pendentes e ≤ 0,26 s nos 3 serviços.
- A cauda que sobra varia muito entre execuções (0,5 s → 4,5 s com o outbox vazio, e 3,5 s já antes do outbox): vem
  de outra parte do caminho (consumidores Kafka, SQL Server sob carga), não do outbox. O painel "Outbox" é o que
  permite separar uma coisa da outra.
- Mesma máquina, poucas execuções por versão: serve para comparar a forma, não para prever produção.
- [x] Relay em lote (acrescentado durante a tarefa): o lote inteiro sai antes de esperar qualquer ack; só o trecho
      antes do primeiro erro conta como publicado; marcação num UPDATE só (unit .NET e Go; lote em ordem no Kafka real
      e erro por mensagem com broker inalcançável no Go).
- [x] Documentação: ADR-005, `events.md` (regra geral do outbox; remover a nota de "repetir republica"),
      `architecture.md`, `conventions.md` (como publicar um evento novo), BACKLOG e `PROGRESS.md`.

## Testes esperados
- Unit: `OutboxEventPublisher`, relay com fakes (ordem, parada no erro, backoff), casos de uso do Orders.
- Integração: Testcontainers (SQL Server + Kafka) em `BuildingBlocks`, Cart, Catalog e store/relay do Orders.
- E2E: o atual, sem mudança de roteiro.

## Fora de escopo
- **Inbox pattern**: os consumidores já são idempotentes por `eventId`/estado; não muda.
- Dead-letter topic (I-11), cancelamento (I-14), consumidores do `shipment.status-changed`.
- CDC/Debezium (só registrado como alternativa na ADR).

## Notas / decisões tomadas durante a tarefa
- Dependências: nenhuma nova. EF Core, Confluent.Kafka, go-mssqldb e franz-go já estão no projeto.
- Mudança de comportamento visível: o checkout não falha mais com o Kafka fora (pedido fica `Pending` até o Kafka voltar).
- Lock do relay: a conexão do `sp_getapplock` fica **fora do pool** (.NET: `Pooling=false`; Go: `SetMaxIdleConns(0)`
  num `sql.DB` só para o lock). Com pool, fechar a conexão não encerra a sessão e o lock ficaria preso.
- Ordem é garantida **por chave** (partição): o teste do relay usa a mesma chave para os eventos cuja ordem confere.
- Marcar publicado sempre pela **lista** de sequências, nunca por faixa (`BETWEEN`): uma transação mais lenta pode
  gravar uma sequência menor depois que o lote foi lido.
- Lote com falha: o que foi confirmado depois do erro fica pendente e é reenviado (duplicata no Kafka, ignorada pelo
  consumidor idempotente). É o preço de nunca deixar uma mensagem posterior publicada sem a anterior.
- Catalog e Orders deixaram de "republicar o resultado" na reentrega: o resultado já está no outbox. O
  `IEventPublisher` resolvido da raiz do DI deixou de ser o Kafka; quem precisa publicar direto (testes que fazem o
  papel de outro serviço) usa `KafkaEventPublisher`.
- A migration `AddOutbox` do Catalog foi gerada de novo: a primeira capturou mudanças não commitadas de outra tarefa
  que estavam na mesma pasta. Regra nova em `conventions.md`: gerar migration com a árvore limpa.
