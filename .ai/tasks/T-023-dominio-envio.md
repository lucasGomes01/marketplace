# T-023 — Domínio do envio (Go)

- **Serviço:** Orders
- **Depende de:** T-012
- **Status:** done

## Objetivo
Dar ao pedido confirmado um ciclo de entrega. Um pedido pode ter itens de vários vendedores, e cada um despacha os
seus: o pedido ganha **um envio por vendedor**, cada um com a própria máquina de status.

## Regras (combinadas com o dono em 2026-10-19)
- Envios nascem junto com o pedido, um por vendedor, na ordem em que o vendedor aparece nos itens; status `AwaitingPicking`.
- Ordem fixa, sem pular etapa: `AwaitingPicking → Picked → Packed → Shipped → Delivered`.
- Só avança envio de pedido `Confirmed` (pedido `Pending`/`Rejected` não é despachado).
- **Vendedor** avança até `Shipped`. **Comprador** confirma `Delivered` (o vendedor não pode marcar como entregue).
- Pedir de novo o status em que o envio já está é aceito sem mudar nada (requisição repetida).
- Cada etapa guarda quando aconteceu (linha do tempo na tela).

## Critérios de aceite
- [x] `New` cria um envio por vendedor, `AwaitingPicking`, na ordem dos itens.
- [x] Vendedor avança passo a passo até `Shipped`; pular etapa, voltar ou pedir `Delivered` → `ErrInvalidTransition`.
- [x] Repetir o status atual não muda nada (nem a data).
- [x] Pedido não confirmado → `ErrInvalidTransition`; vendedor sem envio no pedido → `ErrShipmentNotFound`.
- [x] Comprador confirma recebimento só de envio `Shipped`; repetir é aceito.
- [x] Datas de cada etapa registradas.

## Testes esperados
- Unit: `internal/order/shipment_test.go`.

## Fora de escopo
- API, banco, evento (T-024), telas (T-025), cancelamento (I-14), rastreio (I-15).

## Notas
- Envios nascem no `New` (pedidos antigos ganham envios no backfill da T-024).
