# Contratos de eventos (Kafka)

Decisão e porquês em `decisions/ADR-003-kafka.md`.

## Regras gerais
- **Um tópico por tipo de evento**; o nome do tópico é igual ao `type` do envelope. Use só `.` e `-` no nome
  (misturar `.` e `_` causa colisão nas métricas do Kafka).
- Tópicos são declarados no serviço `kafka-init` do `docker-compose.yml` (criação automática desligada).
  Tópico novo = linha no `kafka-init` + seção neste arquivo.
- **Chave** da mensagem = id do agregado indicado em cada evento (garante ordem por pedido/carrinho).
- **Valor** = envelope JSON em **camelCase**:

```json
{ "eventId": "guid", "type": "cart.checked-out", "occurredAt": "2026-10-13T12:00:00Z", "data": { } }
```

- Headers: `event-type` (= `type`) e `event-id` (= `eventId`), para filtrar sem desserializar.
- Entrega **at-least-once**: todo consumidor é idempotente (guarda `eventId` processado) e só faz commit do offset
  depois de processar.
- **Consumer group = nome do serviço**: `orders-service`, `catalog-service`, `cart-service`.
- Mudar contrato: só adicionar campos opcionais (compatível para trás) ou criar um evento/tópico `...-v2`.

## Tópicos

| Tópico | Produtor | Consumidores (group) | Chave |
|---|---|---|---|
| `cart.checked-out` | Cart | `orders-service` | `cartId` |
| `order.placed` | Orders | `catalog-service` | `orderId` |
| `stock.reserved` | Catalog | `orders-service` | `orderId` |
| `stock.rejected` | Catalog | `orders-service` | `orderId` |
| `shipment.status-changed` | Orders | — (ainda sem consumidor) | `orderId` |

## cart.checked-out (Cart → Orders)
Publicado por `POST /api/cart/checkout` (T-010). O carrinho é esvaziado logo depois de publicar.
`checkoutId` é **determinístico** (derivado do carrinho e da sua última alteração) e é também o `eventId` e o
**id do pedido**: repetir o checkout de um carrinho que não mudou não pode gerar dois pedidos (o Orders ignora id repetido).
```json
{ "checkoutId": "guid", "cartId": "guid", "buyerId": "guid",
  "items": [ { "productId": "guid", "sellerId": "guid", "productName": "string",
               "unitPrice": 10.50, "currency": "BRL", "quantity": 2 } ] }
```

## order.placed (Orders → Catalog)
Publicado pelo Orders ao criar o pedido (T-011); `eventId` = id do pedido. O Catalog reserva o estoque (T-012).
```json
{ "orderId": "guid", "buyerId": "guid", "cartId": "guid",
  "items": [ { "productId": "guid", "quantity": 2 } ] }
```

## stock.reserved (Catalog → Orders)
Estoque de **todos** os itens baixado. `eventId` = `orderId` (um resultado por pedido). O Orders marca `Confirmed`.
```json
{ "orderId": "guid" }
```

## stock.rejected (Catalog → Orders)
Tudo ou nada: se algum item não tem estoque (ou o produto não existe mais), nada é baixado. `reason` é legível
(ex.: "Estoque insuficiente: Furadeira (pedido 3, disponível 2)") e vira o motivo do pedido `Rejected`.
Pedido repetido → o Catalog republica o mesmo resultado (guardado em `StockReservations`), sem baixar de novo.
```json
{ "orderId": "guid", "reason": "string", "productIds": ["guid"] }
```

## shipment.status-changed (Orders → ainda sem consumidor)
Publicado a cada etapa do envio de um vendedor (T-024): `Picked`, `Packed`, `Shipped` (pelo vendedor) e `Delivered`
(o comprador confirma). `eventId` = `orderId:sellerId:status` (determinístico). Repetir a requisição com o mesmo status
**republica** o mesmo evento (corrige uma publicação que falhou depois de gravar; não há outbox, ver I-01).
`changedAt` = quando a etapa aconteceu (não muda na republicação). Futuro: painel do vendedor (I-03), notificações.
```json
{ "orderId": "guid", "buyerId": "guid", "sellerId": "guid", "status": "Shipped", "changedAt": "2026-10-19T12:00:00Z" }
```
