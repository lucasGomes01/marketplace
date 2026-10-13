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
| `order.placed` | Orders | `catalog-service`, `cart-service` | `orderId` |
| `stock.reserved` | Catalog | `orders-service` | `orderId` |
| `stock.rejected` | Catalog | `orders-service` | `orderId` |

## cart.checked-out (Cart → Orders)
```json
{ "cartId": "guid", "buyerId": "guid",
  "items": [ { "productId": "guid", "sellerId": "guid", "productName": "string",
               "unitPrice": 10.50, "currency": "BRL", "quantity": 2 } ] }
```

## order.placed (Orders → Catalog, Cart)
```json
{ "orderId": "guid", "buyerId": "guid", "cartId": "guid",
  "items": [ { "productId": "guid", "quantity": 2 } ] }
```

## stock.reserved (Catalog → Orders)
```json
{ "orderId": "guid" }
```

## stock.rejected (Catalog → Orders)
```json
{ "orderId": "guid", "reason": "string", "productIds": ["guid"] }
```
