# Arquitetura

```
             ┌──────────────┐
  React ───▶ │ Gateway YARP │ :5000   (/api/users, /api/auth, /api/products, /api/cart, /api/orders)
  :5173      └──────┬───────┘
     ┌──────────────┼───────────────┬───────────────┐
     ▼              ▼               ▼               ▼
  Users :5001   Catalog :5002   Cart :5003     Orders :5004 (Go)
  UsersDb       CatalogDb       CartDb         OrdersDb         ← SQL Server (1 banco por serviço)
                    │               │               │
                    └──────── Kafka (um tópico por evento) ────────┘
```

## Serviços
| Serviço | Stack | Responsabilidade | Porta local |
|---|---|---|---|
| Gateway | .NET 10 + YARP | Roteamento, CORS, (futuro) rate limit | 5000 |
| Users | .NET 10 + EF Core | Cadastro, login, emissão de JWT | 5001 |
| Catalog | .NET 10 + EF Core | Produtos e estoque | 5002 |
| Cart | .NET 10 + EF Core | Carrinho do comprador, checkout | 5003 |
| Orders | Go (stdlib + go-mssqldb + franz-go) | Pedidos e ciclo de vida | 5004 |
| Web | React + TS + Vite | UI | 5173 |

No Docker, cada serviço escuta em `8080`; o gateway é exposto em `localhost:5000`.

## Estrutura de cada serviço .NET (camadas SOLID — ADR-004)
```
Api ──► Application ──► Domain          (as setas só apontam para dentro)
 │           ▲
 └──► Infrastructure (implementa as interfaces da Application)
```
- `X.Domain`: entidades, value objects e regras. Só a biblioteca padrão.
- `X.Application`: casos de uso (`IHandler<TRequest, TResponse>` → `Result<T>`), **portas** em `Abstractions/`
  (repositórios, hasher, emissor de token, publicador de eventos) e DTOs. Uma pasta por caso de uso (`Register/`, `Login/`).
- `X.Infrastructure`: EF Core (`Persistence/`: DbContext, configurações, migrations, repositórios), segurança, Kafka.
  Expõe `AddXInfrastructure(configuration)`.
- `X.Api`: endpoints finos em `Endpoints/` + `Program.cs` (composition root).
- `tests/X/X.UnitTests`: domínio, casos de uso com fakes em memória e **testes de arquitetura** (regra de dependência).
- `tests/X/X.IntegrationTests`: `WebApplicationFactory` + **Testcontainers** (SQL Server e Kafka reais).
- Compartilhados: `BuildingBlocks.Application` (`Result`, `Error`, `IHandler`, `AddHandlersFrom`),
  `BuildingBlocks.Api` (`ToHttpResult`), `BuildingBlocks.Auth` (JWT), `BuildingBlocks.Messaging` (Kafka).
- Referência completa: serviço **Users**.

## Orders (Go)
```
cmd/orders/main.go        wiring
internal/order/           domínio (Order, status, regras): testes unitários
internal/api/             handlers HTTP (net/http, roteamento do Go 1.22+)
internal/store/           repositório SQL Server
internal/messaging/       consumer/producer Kafka (franz-go)
```

## Comunicação
- **Síncrona (HTTP):** só quando a resposta é necessária na hora.
  Ex.: o Cart consulta o Catalog (`GET /products/{id}`, timeout 5 s) para obter preço e estoque ao adicionar um item.
  Dependência fora do ar → `Error.Unavailable` → **503** (nunca 500). URL base em `Services:<Serviço>`.
- **Assíncrona (Kafka):** para tudo que é consequência de um fato. Tópicos, chaves e contratos em `events.md`.
- Um tópico por tipo de evento (`cart.checked-out`, `order.placed`...). Chave = id do agregado (ordem por pedido).
  Cada serviço consome com seu próprio consumer group (`orders-service`, `catalog-service`...).
- Kafka UI em http://localhost:8080 para ver tópicos, mensagens e offsets dos grupos.
- Consumidores devem ser **idempotentes** (guardar o `eventId` já processado).
- .NET: `AddKafkaEventConsumer<TEvent,THandler>(tópico, grupo)` (BuildingBlocks.Messaging) — escopo de DI por mensagem,
  commit só depois de processar, nova tentativa com backoff, JSON inválido/`InvalidEventException` registrados e pulados.
  Go (Orders): mesmo comportamento em `internal/messaging`.

## Fluxo de checkout (implementado até a T-012)
1. `POST /api/cart/checkout` → o Cart publica `cart.checked-out` (itens com snapshot de preço) e esvazia o carrinho.
   Responde `202 { checkoutId, orderId }` (o pedido nasce de forma assíncrona; `orderId` = `checkoutId`).
2. O Orders consome → cria `Order(Pending)` → publica `order.placed`.
3. O Catalog consome `order.placed` → baixa o estoque (tudo ou nada, na mesma transação da `StockReservation`;
   `rowversion` em `Products` resolve a disputa pela última unidade) → publica `stock.reserved` ou `stock.rejected`.
4. O Orders consome → `Confirmed` ou `Rejected`.

## Observabilidade (T-022)
- OpenTelemetry em todos os serviços (.NET: `BuildingBlocks.Observability`; Go: `internal/telemetry`), exportando OTLP
  para o serviço `observability` (Grafana + Prometheus + Tempo + Loki). Sem `OTEL_EXPORTER_OTLP_ENDPOINT`, nada é exportado.
- O trace atravessa o Kafka: quem publica grava `traceparent` (W3C) nos headers; quem consome continua o trace.
- Métricas de negócio: `marketplace.orders{status}`, `marketplace.order.settle.duration`, `marketplace.stock.reservations{outcome}`.
- Testes de carga (k6) vão para o mesmo Prometheus com `versao`/`cenario` — painel "Marketplace" compara versões.

## Autenticação
O Users emite o JWT (HS256 no MVP; chave em config). Cada serviço valida o JWT (mesma chave e issuer).
Claims: `sub` (userId), `email`, `role` — **um claim por papel ativo** (`["Buyer"]` ou `["Buyer","Seller"]`).
Ao ativar o perfil de vendedor (`POST /api/users/me/seller`) a resposta traz um token novo com o papel `Seller`.

Tudo vive em `src/building-blocks/BuildingBlocks.Auth`. Para proteger um serviço .NET:
```csharp
builder.Services.AddMarketplaceJwtAuth(builder.Configuration);   // lê a seção "Jwt"
app.UseAuthentication(); app.UseAuthorization();
app.MapPost("/products", ...).RequireAuthorization(MarketplacePolicies.Seller);
var userId = httpContext.User.GetUserId();
```
O Orders (Go) deve validar o mesmo token: HS256, `iss=marketplace-users`, `aud=marketplace`, a mesma chave.
