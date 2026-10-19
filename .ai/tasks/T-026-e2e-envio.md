# T-026 — E2E do envio

- **Serviço:** Infra (Playwright)
- **Depende de:** T-025
- **Status:** done

## Objetivo
O caminho feliz do E2E (T-016) vai até o fim: depois da compra confirmada, a vendedora despacha e o comprador
confirma o recebimento, tudo contra o stack real (gateway, Orders em Go, Kafka, SQL Server).

## Critérios de aceite
- [x] Vendedora entra, abre "Despachar" e marca separado → embalado → enviado; o envio passa para "Enviados".
- [x] Comprador entra, vê o envio "Enviado" no pedido, clica "Recebi" e vê "Entregue".
- [x] "Meus pedidos" mostra "Entrega: Entregue".
- [ ] Roda no CI como o E2E de antes (mesmo spec, sem job novo) — confirmar quando o PR rodar.

## Fora de escopo
- Pedido com dois vendedores no E2E (coberto nos testes de unidade/integração).

## Notas
- Verde localmente em 2026-10-19 (11 s). Antes foi preciso resetar os offsets de `orders-service` e `catalog-service` para o fim: o Kafka local tinha ~140 mil eventos de um teste de estresse antigo na fila.
- Resetar offset exige o grupo inativo (serviço parado e sessão expirada). O broker travou em `COORDINATOR_NOT_AVAILABLE` e precisou de restart.
