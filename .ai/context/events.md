# Contratos de eventos (RabbitMQ)

Exchange: `marketplace.events` (topic, durable). Payload JSON em **camelCase**. Todo evento tem este envelope:

```json
{ "eventId": "guid", "type": "cart.checked_out", "occurredAt": "2026-09-29T12:00:00Z", "data": { } }
```

Mudar um contrato exige compatibilidade para trás (só adicionar campos opcionais) ou criar uma versão `vN` do evento.

## cart.checked_out (Cart → Orders)
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
