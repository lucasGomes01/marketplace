# T-010 — Mensageria Kafka + checkout do carrinho

- **Serviço:** BuildingBlocks.Messaging + Cart
- **Depende de:** T-008
- **Status:** done

## Objetivo
"Finalizar pedido": o Cart publica `cart.checked-out` no Kafka com o snapshot do carrinho e esvazia o carrinho.

## Desenho
- `IEventPublisher` (porta) em `BuildingBlocks.Application` — a Application não conhece Kafka.
- `KafkaEventPublisher` em `BuildingBlocks.Messaging` (Confluent.Kafka): envelope JSON camelCase
  `{ eventId, type, occurredAt, data }`, chave = id do agregado, headers `event-type` e `event-id`,
  produtor idempotente com `acks=all`.
- **Id do checkout determinístico**: derivado de `(cartId, updatedAt)` do carrinho. Repetir o checkout de um carrinho
  que não mudou gera o **mesmo id** → o Orders (T-011) usa esse id como **id do pedido** e ignora duplicatas.
  Também é o `eventId` do envelope.
- Ordem: **publica primeiro**, depois esvazia o carrinho. Kafka fora → `503` e o carrinho fica intacto.
  (Se a gravação falhar depois de publicar, uma nova tentativa gera o mesmo id e não duplica o pedido.)
  A garantia completa fica para o padrão outbox (ideia I-01).
- O carrinho é esvaziado **no checkout** (antes o plano era esperar `order.placed`): os itens agora vivem no pedido.

## Rota
`POST /api/cart/checkout` (login) → `202 Accepted { checkoutId, orderId }` (`orderId` = `checkoutId`).

## Critérios de aceite
- [x] Carrinho vazio ou inexistente → `400 cart.empty`.
- [x] Publica em `cart.checked-out`, chave `cartId`, com `{ checkoutId, cartId, buyerId, items[...] }`
      (produto, vendedor, nome, preço unitário, moeda, quantidade) — contrato em `context/events.md`.
- [x] Mesmo carrinho → mesmo `checkoutId`; carrinho alterado → outro id.
- [x] Depois de publicar, o carrinho fica vazio.
- [x] Kafka indisponível → `503 events.unavailable` e o carrinho não é esvaziado.
- [x] Testes: unitários do handler (publicador falso), da fábrica de mensagens (envelope, chave, headers) e
      integração com **Kafka real** (Testcontainers) lendo a mensagem publicada.

## Fora de escopo
- Consumir o evento (T-011), outbox (I-01), revalidar preços no Catalog no checkout.

## Notas
- 2026-10-13 (concluída). BuildingBlocks.UnitTests 16 (+3 do envelope Kafka), Cart.UnitTests 54 (+5 checkout, +1 posição), Cart.IntegrationTests 16 (+3 com **Kafka real** via Testcontainers lendo a mensagem do tópico).
- **Bug encontrado:** a ordem dos itens do carrinho não era garantida ao carregar do banco (EF + ordenação de `uniqueidentifier` do SQL Server) — a tela podia embaralhar os itens ao recarregar. `CartItem.Position` + `Items` ordenado; migration `CartItemPosition` preenche posições dos carrinhos existentes.
- Kafka dos testes igual à produção: tópicos criados antes, criação automática desligada.
- **Instabilidade não resolvida (I-10):** em 2 de ~8 rodadas da suíte inteira, testes de integração do Cart falharam (1× só o de checkout, 1× a classe inteira). Não reproduzi depois; causa não confirmada (suspeita: carga de vários containers ao mesmo tempo). O CI passou a guardar os resultados detalhados em caso de falha.
- Validado no compose via gateway: checkout 202 → carrinho vazio → mensagem em `cart.checked-out` com headers e snapshot.
- Mudança de plano registrada: o carrinho é esvaziado no checkout (não mais ao receber `order.placed`); `events.md`, `architecture.md` e T-012 atualizados.
