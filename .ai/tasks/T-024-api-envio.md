# T-024 — API, persistência e evento do envio

- **Serviço:** Orders (Go) + Gateway + Infra
- **Depende de:** T-023
- **Status:** done

## Objetivo
Expor o ciclo de envio da T-023: o vendedor vê e avança os envios dele, o comprador confirma o recebimento, e cada
mudança vira o evento `shipment.status-changed`.

## Contrato HTTP (via gateway em `/api/...`)
- `GET /shipments` — **Seller.** Envios do vendedor em pedidos `Confirmed`, mais recentes primeiro (até 50), cada um
  só com os itens dele: `{ orderId, status, pickedAt, packedAt, shippedAt, deliveredAt, items, itemCount, total, currency, orderedAt }`.
- `PUT /shipments/{orderId}/status` — **Seller.** Corpo `{ "status": "Picked" | "Packed" | "Shipped" }` → 200 com o envio.
- `POST /orders/{id}/shipments/{sellerId}/delivery` — **comprador do pedido.** "Recebi" → 200 com o pedido.
- `GET /orders` e `GET /orders/{id}` ganham `shipments: [{ sellerId, status, pickedAt, packedAt, shippedAt, deliveredAt }]`
  (datas `null` enquanto a etapa não aconteceu).
- Erros (ProblemDetails com `code`): 400 `shipment.invalid_status`, 403 sem papel Seller, 404 `shipment.not_found`
  (inclusive pedido de outro comprador/vendedor), 409 `shipment.invalid_transition`, 409 `shipment.conflict`
  (outra requisição mudou o envio ao mesmo tempo).

## Persistência
- Tabela `OrderShipments` (PK `OrderId, SellerId`; `Position` = ordem do vendedor nos itens; uma data por etapa).
- **Backfill no startup**, idempotente: pedidos que já existiam ganham um envio `AwaitingPicking` por vendedor.
- Atualização condicional (`WHERE Status = <anterior>`): duas requisições ao mesmo tempo → uma vence, a outra 409.

## Evento
- `shipment.status-changed`, chave `orderId`, `eventId` = `orderId:sellerId:status` (determinístico).
- Publicado depois de gravar. Repetir a requisição com o mesmo status republica (corrige uma publicação que falhou).
  Sem consumidores por enquanto (painel do vendedor I-03, notificações).

## Critérios de aceite
- [x] App (unit): vendedor avança e publica; repetir republica sem gravar de novo; vendedor sem envio → not found;
      comprador confirma só o próprio pedido; lista do vendedor só com pedidos confirmados.
- [x] Store (SQL Server real): envios gravados com o pedido e lidos na ordem; atualização condicional → `ErrConflict`;
      lista por vendedor; backfill de pedido antigo.
- [x] API (unit): rotas, papel Seller, códigos de erro, forma do JSON.
- [x] Gateway: rota `/api/shipments` (teste de integração do gateway).
- [x] Tópico no `kafka-init` + `events.md`.

## Fora de escopo
- Telas (T-025), cancelamento (I-14), rastreio (I-15), consumidores do evento.

## Notas
- Testes Go no Windows com Testcontainers: `TESTCONTAINERS_RYUK_DISABLED=true` (o Ryuk não subiu: "Started matched 0 times").
- O backfill roda no `schema` do startup; com várias instâncias subindo juntas a PK impede duplicata (uma delas falharia e reiniciaria).
