# Convenções

## Geral
- Código, nomes de classes e testes em **inglês**; documentação (`.ai`, README) em **português**.
- Commits em Conventional Commits + id da tarefa: `feat(cart): add item to cart [T-007]`.
- Branch: `feat/T-XXX-slug`, `fix/...`, `chore/...`. PR pequeno, uma tarefa por PR; o CI precisa estar verde para mergear.

## TDD
- Nenhum código de produção sem um teste falhando antes.
- O nome do teste descreve o comportamento: `Adding_same_product_twice_increases_quantity`
  (C#) / `TestAddingSameProductTwiceIncreasesQuantity` (Go).
- Arrange / Act / Assert separados por linha em branco.
- Pirâmide: a maioria dos testes é unitária no Domain; os de integração cobrem endpoint + banco real (Testcontainers).
  Nada de mockar o DbContext.
- Um container por classe de teste via `IClassFixture` / `ICollectionFixture`, para manter a suíte rápida.

### Padrão de teste de integração (.NET) — referência: `tests/Users/Users.IntegrationTests`
- `XApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` sobe um `MsSqlContainer` e injeta
  `ConnectionStrings:Default` + `Database:MigrateOnStartup=true` via `UseSetting`.
- Uma `[CollectionDefinition]` com `ICollectionFixture<XApiFactory>`; toda classe de teste da API usa `[Collection(...)]`
  (um único container para o projeto inteiro).
- Dados únicos por teste (ex.: email com `Guid`), sem limpar o banco entre testes.
- Para verificar o estado no banco: `factory.WithDbContext(db => ...)`.
- xUnit 2.x: implementar `IAsyncLifetime` explicitamente (retorna `Task`) porque a factory já tem `DisposeAsync(): ValueTask`.

### Padrão de endpoint (.NET) — referência: `Users.Api/Features/Users/RegisterUser.cs`
- Classe estática por caso de uso com `Request`/`Response` aninhados e `MapXxx(this IEndpointRouteBuilder)`.
- Regras da entidade → `DomainException(message, field)` → `ValidationProblem` com a chave `field`.
- Conflito de unicidade: checar antes **e** tratar `SqlException` 2601/2627 (corrida).
- Migrations em `Infrastructure/Migrations`: `dotnet tool run dotnet-ef migrations add <Nome> -p src/services/X/X.Api -o Infrastructure/Migrations`.

## C# / .NET
- .NET 10, Nullable habilitado, `record` para DTOs e eventos.
- O domínio protege as invariantes: construtor/factory valida e lança `DomainException`; setters privados.
- Minimal APIs agrupadas por feature com `MapGroup`. Erros → `ProblemDetails` (RFC 9457).
- Dinheiro: `decimal` dentro do value object `Money`; nunca `double`.
- IDs: `Guid` v7 (`Guid.CreateVersion7()`).
- EF Core: configuração com `IEntityTypeConfiguration<T>`, migrations no projeto Api.

## Go
- Stdlib primeiro. Dependências permitidas: `github.com/microsoft/go-mssqldb`,
  `github.com/rabbitmq/amqp091-go`, `github.com/testcontainers/testcontainers-go`.
- `go vet` e `gofmt` limpos. Erros embrulhados com `fmt.Errorf("...: %w", err)`.
- Testes table-driven quando houver variações.

## React / TS
- Componentes funcionais, TS estrito. Chamadas HTTP ficam em `src/api/*` (fetch tipado), nunca direto no componente.
- Testes com Vitest + Testing Library (adicionar quando a primeira tela for criada).

## API
- Rotas no plural e em minúsculas: `/products`, `/products/{id}`, `/cart/items`.
- Tudo passa pelo gateway: o front chama `/api/<serviço>/...`.
