# T-011 — Orders consome o checkout, grava o pedido e publica `order.placed`

- **Serviço:** Orders (Go)
- **Depende de:** T-009, T-010
- **Status:** done

## Objetivo
Fechar o primeiro elo assíncrono: `cart.checked-out` (Kafka) → pedido gravado no SQL Server → `order.placed` (Kafka).
E dar ao comprador as rotas para ver os pedidos.

## Desenho (Go, pacotes em `internal/`)
| Pacote | Papel |
|---|---|
| `order` | domínio (T-009) |
| `app` | casos de uso: `PlaceOrder(evento)` e consultas; define as **interfaces** que usa (repositório, publicador) |
| `store` | SQL Server (`go-mssqldb`): cria o banco/tabelas se não existirem, grava pedido+itens numa transação |
| `messaging` | Kafka (`franz-go`): consumidor do grupo `orders-service` e publicador com o mesmo envelope dos serviços .NET |
| `auth` | valida o JWT HS256 dos outros serviços (stdlib: `crypto/hmac`) — mesmo issuer/audience/chave |
| `api` | `GET /orders` e `GET /orders/{id}` (só o dono vê) |

## Regras
- **Idempotência:** o id do pedido é o `checkoutId` do evento. Evento repetido → não cria outro pedido, mas
  **republica** `order.placed` (se a publicação anterior falhou, o fluxo continua).
- `order.placed` usa `eventId` = id do pedido e chave = id do pedido.
- Offset só é confirmado **depois** de gravar e publicar (at-least-once).
- Evento inválido (JSON quebrado, regra de domínio violada) é registrado no log e **pulado** (não trava a fila).
  Dead-letter fica como ideia.
- Preço chega como decimal (`199.90`) e vira centavos sem `float` (parse da string do número).

## Rotas (via gateway `/api/orders...`, exigem login)
- `GET /orders` → pedidos do comprador logado, mais recentes primeiro.
- `GET /orders/{id}` → `200` se for do comprador; `404` caso contrário (não revela pedidos alheios).
- Pedido: `{ id, status, rejectionReason, items[{productId, sellerId, productName, unitPrice, currency, quantity, subtotal}], itemCount, total, currency, createdAt, updatedAt }`.

## Critérios de aceite
- [x] Testes unitários: JWT (válido, assinatura errada, expirado, issuer/audience errados, `alg: none`), dinheiro
      (decimal ↔ centavos), `PlaceOrder` (cria, duplicado não duplica e republica, inválido → erro), API (401, lista
      só do dono, 404 para pedido alheio, formato JSON).
- [x] Testes de integração (testcontainers-go): repositório com **SQL Server real**; consumidor+publicador com **Kafka real**.
- [x] No compose: checkout pelo gateway → pedido aparece em `GET /api/orders` → mensagem em `order.placed`.
- [x] `gofmt`, `go vet`, `go test -race` limpos; CI com Docker para os testes de integração.

## Fora de escopo
- Baixa de estoque e confirmação/recusa (T-012), telas (T-015), dead-letter topic.

## Notas
- 2026-10-13 (concluída). Testes Go: unitários de `auth` (JWT HS256 sem lib externa), `order` (centavos ↔ decimal sem float), `app` (PlaceOrder idempotente) e `api`; integração do `store` com **SQL Server real** (passou local). Integração do `messaging` com Kafka real: passa local e no CI (ver correção abaixo).
- SQL Server do teste: espera aumentada para 4 min (o padrão de 60 s estourou com a máquina carregada).
- Validado no compose: checkout → pedido em ~1 s (`Pending`, total certo) → `order.placed` no Kafka; `/api/orders` sem login 401; pedido de outra pessoa 404.
- **Idempotência provada:** consumer group voltado para o início do tópico → todos os checkouts reentregues → continuam 2 pedidos (nenhum duplicado), `order.placed` republicado. Mensagem inválida antiga (`{"hello":"kafka"}` da T-017) é pulada sem travar a fila.
- Dinheiro sai no JSON como número com 2 casas (`399.80`), igual às APIs .NET.
- Dockerfile do Orders passou a copiar `go.sum`.
- **Correção (mesmo dia):** a primeira versão desta nota dizia que o teste de Kafka do Go "não roda dentro de container no Docker Desktop" e que seria "validado no CI". **Estava errado:** o CI falhou com o mesmo erro. A causa real era a imagem `confluent-local:7.9.0` — o módulo testcontainers-go v0.44 só suporta `confluent-local` e é testado com a 7.5.0 (a 7.9.0 anuncia 0.0.0.0; `apache/kafka` nem tem o shell que o módulo injeta). Com a 7.5.0 os 3 cenários passam local (dentro de container) e no CI.
