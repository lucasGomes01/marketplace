# T-009 — Domínio do pedido (Go)

- **Serviço:** Orders (`src/services/Orders/internal/order`)
- **Depende de:** T-001
- **Status:** done

## Objetivo
Modelar o pedido e o ciclo de status em Go puro (stdlib), sem banco nem Kafka. É o primeiro código de negócio em Go.

## Regras
- O pedido nasce do checkout do carrinho com um **snapshot** dos itens (produto, vendedor, nome, preço unitário, quantidade).
- Precisa de comprador, carrinho de origem e pelo menos 1 item; quantidade 1..99; preço > 0; uma moeda só;
  o mesmo produto não aparece duas vezes.
- Dinheiro em **centavos (`int64`)** — nunca `float`.
- Status: `Pending` → `Confirmed` | `Rejected`. Recusa exige motivo.
- **Transições idempotentes:** confirmar um pedido já confirmado (ou recusar um já recusado) não é erro — eventos
  do Kafka podem chegar repetidos. Confirmar um recusado (ou o contrário) é erro.
- IDs: UUID v7 (ordenável por tempo), gerado sem dependências externas.

## Critérios de aceite
- [x] `order.New(...)` valida tudo acima e devolve erros com o **campo** (`DomainError{Field, Message}`).
- [x] `Total()` soma `preço × quantidade`; `ItemCount()` soma as quantidades.
- [x] `Confirm(now)` / `Reject(reason, now)` com as regras de transição e `UpdatedAt`.
- [x] `NewID()` gera UUID v7 válido (versão 7, variante RFC 4122) e crescente no tempo.
- [x] Testes table-driven; `gofmt`, `go vet` e `go test -race` limpos (via Docker, já que Go não está instalado na máquina).

## Fora de escopo
- Persistência, HTTP e Kafka (T-011, T-012).

## Notas
- 2026-10-13 (concluída): 23 testes (10 funções + 13 subcasos de validação), `gofmt`/`vet`/`-race` limpos via Docker.
- Para `-race` use a imagem `golang:1.26` (Debian): o detector de corridas precisa de cgo, ausente na `-alpine`.
- Dinheiro em centavos `int64`. O evento `cart.checked-out` traz `unitPrice` decimal (ex.: 10.50): a conversão para centavos fica no adaptador de mensageria (T-011), com arredondamento explícito.
- `NewID` (UUID v7) escrito com `crypto/rand` — sem dependência externa.
