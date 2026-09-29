# Arquitetura

```
             ┌──────────────┐
  React ───▶ │ Gateway YARP │ :5000   (/api/users, /api/catalog, /api/cart, /api/orders)
  :5173      └──────┬───────┘
     ┌──────────────┼───────────────┬───────────────┐
     ▼              ▼               ▼               ▼
  Users :5001   Catalog :5002   Cart :5003     Orders :5004 (Go)
  UsersDb       CatalogDb       CartDb         OrdersDb         ← SQL Server (1 banco por serviço)
                    │               │               │
                    └──────── RabbitMQ (exchange marketplace.events) ────────┘
```

## Serviços
| Serviço | Stack | Responsabilidade | Porta local |
|---|---|---|---|
| Gateway | .NET 10 + YARP | Roteamento, CORS, (futuro) rate limit | 5000 |
| Users | .NET 10 + EF Core | Cadastro, login, emissão de JWT | 5001 |
| Catalog | .NET 10 + EF Core | Produtos e estoque | 5002 |
| Cart | .NET 10 + EF Core | Carrinho do comprador, checkout | 5003 |
| Orders | Go (stdlib + go-mssqldb + amqp091-go) | Pedidos e ciclo de vida | 5004 |
| Web | React + TS + Vite | UI | 5173 |

No Docker, cada serviço escuta em `8080`; o gateway é exposto em `localhost:5000`.

## Estrutura de cada serviço .NET
- `X.Domain`: entidades, value objects e regras. **Sem** dependência de EF ou ASP.NET. É o alvo dos testes unitários.
- `X.Api`: Minimal APIs, EF Core (DbContext, migrations) e mensageria. Organizado por feature
  (`Features/Products/CreateProduct.cs`), não por camada técnica.
- `tests/X/X.UnitTests`: xUnit, domínio puro, rápido.
- `tests/X/X.IntegrationTests`: `WebApplicationFactory` + **Testcontainers** (SQL Server e RabbitMQ reais).

## Orders (Go)
```
cmd/orders/main.go        wiring
internal/order/           domínio (Order, status, regras): testes unitários
internal/api/             handlers HTTP (net/http, roteamento do Go 1.22+)
internal/store/           repositório SQL Server
internal/messaging/       consumer/publisher RabbitMQ
```

## Comunicação
- **Síncrona (HTTP):** só quando a resposta é necessária na hora.
  Ex.: o Cart consulta o Catalog para obter preço e estoque ao adicionar um item.
- **Assíncrona (RabbitMQ):** para tudo que é consequência de um fato. Os contratos ficam em `events.md`.
- Exchange `marketplace.events` (topic). Routing key = nome do evento (`cart.checked_out`).
  Cada serviço tem sua própria fila (`orders.cart-checked-out`, `catalog.order-placed`...).
- Consumidores devem ser **idempotentes** (guardar o `eventId` já processado).

## Fluxo de checkout (alvo do MVP)
1. `POST /api/cart/checkout` → o Cart publica `cart.checked_out` (itens com snapshot de preço).
2. O Orders consome → cria `Order(Pending)` → publica `order.placed`.
3. O Catalog consome `order.placed` → baixa o estoque → publica `stock.reserved` ou `stock.rejected`.
4. O Orders consome → `Confirmed` ou `Rejected`. O Cart consome `order.placed` → limpa o carrinho.

## Autenticação
O Users emite o JWT (HS256 no MVP; chave em config). Cada serviço valida o JWT (mesma chave e issuer).
Claims: `sub` (userId), `email`, `role` (`Buyer`|`Seller`).
