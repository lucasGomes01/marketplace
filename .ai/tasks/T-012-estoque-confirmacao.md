# T-012 — Baixa de estoque e confirmação do pedido

- **Serviço:** Catalog (.NET) + Orders (Go) + BuildingBlocks.Messaging (consumidor .NET)
- **Depende de:** T-011
- **Status:** done

## Objetivo
Fechar o fluxo: `order.placed` → Catalog baixa o estoque → `stock.reserved` | `stock.rejected` → Orders marca o
pedido como `Confirmed` | `Rejected`.

## Regras
- **Tudo ou nada:** se algum item não tem estoque (ou o produto não existe mais), nada é baixado e o pedido é recusado
  com um motivo legível (ex.: "Estoque insuficiente: Furadeira de impacto 700W (pedido 3, disponível 2)").
- **Idempotência no Catalog:** cada pedido gera uma `StockReservation` (chave = `orderId`) gravada **na mesma
  transação** da baixa. Evento repetido → não baixa de novo; republica o mesmo resultado.
- **Última unidade disputada:** `rowversion` em `Products`. Se duas reservas mexem no mesmo produto ao mesmo tempo,
  uma falha por concorrência e é **reprocessada** (o consumidor tenta de novo): na nova leitura o estoque já baixou e
  ela é recusada corretamente. Nunca estoque negativo.
- Publica **depois** de gravar. Se a publicação falhar, o consumidor tenta de novo e a reserva existente é republicada.
- **Orders:** `Confirm`/`Reject` já são idempotentes (T-009). Pedido inexistente → registra e pula.

## Peças novas
- `BuildingBlocks.Application.Messaging`: `IIntegrationEventHandler<TEvent>` (porta) e `InvalidEventException`.
- `BuildingBlocks.Messaging`: consumidor Kafka genérico (`BackgroundService`) com escopo de DI por mensagem,
  commit só depois de processar, nova tentativa com backoff, e mensagens inválidas registradas e puladas.
- Catalog: `StockReservation` (domínio), `ReserveStockHandler` (Application), persistência e migration (rowversion + tabela de reservas).
- Orders: consome `stock.reserved` e `stock.rejected` no mesmo grupo; `UpdateStatus` no store.

## Critérios de aceite
- [x] Unitários do `ReserveStockHandler`: reserva tudo; recusa tudo-ou-nada; produto inexistente recusa; evento repetido
      republica sem baixar de novo; conflito de concorrência propaga para nova tentativa.
- [x] Integração Catalog com **Kafka real**: publicar `order.placed` → estoque baixa → `stock.reserved` publicado;
      repetido não baixa de novo; sem estoque → `stock.rejected` com motivo.
- [x] Integração Catalog: duas reservas simultâneas da **última unidade** → uma confirma, a outra recusa, estoque 0.
- [x] Go: `ConfirmOrder`/`RejectOrder` (unit) e `UpdateStatus` (SQL Server real).
- [x] Compose: checkout → pedido `Confirmed` e estoque baixado; checkout sem estoque → `Rejected` com motivo.

## Fora de escopo
- Devolver estoque em cancelamento, reserva com expiração, telas (T-015).

## Notas
- Verificado no compose (2026-10-14): checkout com estoque → `Confirmed` e estoque 5→3; dois compradores na última
  unidade ao mesmo tempo → um `Confirmed`, outro `Rejected` com "Estoque insuficiente: … (pedido 1, disponível 0)", estoque 0.
- O teste da última unidade confere o resultado no banco (esperar o Kafka com prazo fixo deixava o teste instável).
- Go: `WithWaitStrategyAndDeadline` no SQL Server dos testes — `WithWaitStrategy` tem prazo oculto de 60 s.
