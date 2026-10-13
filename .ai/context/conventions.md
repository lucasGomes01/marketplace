# Convenções

## Geral
- Código, nomes de classes e testes em **inglês**; documentação (`.ai`, README) em **português**.
- Commits em Conventional Commits + id da tarefa: `feat(cart): add item to cart [T-007]`.
- Commits saem **só no nome do dono do repositório**: agentes de IA não adicionam `Co-Authored-By` nem outra atribuição.
- Branch: `feat/T-XXX-slug`, `fix/...`, `chore/...`. PR pequeno, uma tarefa por PR; o CI precisa estar verde para mergear.

## TDD
- Nenhum código de produção sem um teste falhando antes.
- O nome do teste descreve o comportamento: `Adding_same_product_twice_increases_quantity`
  (C#) / `TestAddingSameProductTwiceIncreasesQuantity` (Go).
- Arrange / Act / Assert separados por linha em branco.
- Pirâmide: a maioria dos testes é unitária (Domain e handlers da Application com fakes); os de integração cobrem endpoint + banco real (Testcontainers).
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

### SOLID: passo a passo de um caso de uso novo (.NET) — referência: serviço Users, ADR-004
1. **Domain** (se houver regra nova): teste unitário → entidade/value object. Violação → `DomainException(message, field)`.
2. **Application**: teste do handler com **fakes em memória** das portas → `XxxCommand`/`XxxQuery` (record) +
   `XxxHandler : IHandler<XxxCommand, XxxResponse>` na pasta do caso de uso. Precisa de algo externo? Crie uma
   **porta pequena** em `Abstractions/` (I de SOLID), nunca injete `DbContext`, `HttpClient` ou Kafka no handler.
3. Falha esperada → `return Error.Validation(field, code, message)` / `Error.Conflict(...)` / `Error.NotFound(...)` /
   `Error.Unauthorized(...)`. Códigos estáveis no formato `campo.motivo` (`password.too_short`). Exceção só para o
   inesperado. `DomainException` vira `Error.Validation(ex.Field, ...)` no handler.
4. **Infrastructure**: implemente a porta (ex.: repositório EF) e registre em `AddXInfrastructure`.
   Corridas de unicidade: checar antes **e** traduzir `SqlException` 2601/2627 numa exceção da porta.
5. **Api**: `app.UseMarketplaceExceptionHandler()` no Program; endpoint em `Endpoints/` que só faz `request → command → handler → result.ToHttpResult(...)`.
   O handler é registrado sozinho (`AddHandlersFrom`); nada mais muda (O de SOLID).
6. Rode os testes de arquitetura: eles falham se uma camada interna referenciar uma externa.

- Migrations: `dotnet tool run dotnet-ef migrations add <Nome> -p src/services/X/X.Infrastructure -s src/services/X/X.Api -o Persistence/Migrations`.

## C# / .NET
- .NET 10, Nullable habilitado, `record` para DTOs e eventos.
- O domínio protege as invariantes: construtor/factory valida e lança `DomainException`; setters privados.
- Minimal APIs finas (ver passo a passo SOLID acima). Erros → `ProblemDetails` (RFC 9457) com `code` nas extensões.
- Dinheiro: `decimal` dentro do value object `Money`; nunca `double`.
- IDs: `Guid` v7 (`Guid.CreateVersion7()`).
- EF Core: configuração com `IEntityTypeConfiguration<T>`, migrations em `X.Infrastructure/Persistence/Migrations`.

## Go
- Stdlib primeiro. Dependências permitidas: `github.com/microsoft/go-mssqldb`,
  `github.com/twmb/franz-go` (Kafka), `github.com/testcontainers/testcontainers-go`.
- `go vet` e `gofmt` limpos. Erros embrulhados com `fmt.Errorf("...: %w", err)`.
- Testes table-driven quando houver variações.

## React / TS
- Componentes funcionais, TS estrito. Chamadas HTTP ficam em `src/api/*` (fetch tipado), nunca direto no componente.
- Testes com Vitest + Testing Library + MSW (`npm test`). Teste a **aplicação inteira** numa rota com `renderApp(rota, { loggedIn })` (`src/test/render.tsx`); a API falsa fica em `src/test/server.ts` e cada teste sobrescreve com `server.use(...)`.
- Mensagens da API chegam em inglês: traduza em `src/api/messages.ts` (desconhecida passa como veio).
- Rotas protegidas ficam dentro de `<Route element={<RequireAuth />}>`; sessão via `useAuth()`.

## API
- Rotas no plural e em minúsculas: `/products`, `/products/{id}`, `/cart/items`.
- Tudo passa pelo gateway, que roteia por **recurso**: o front chama `/api/<recurso>/...` (ex.: `/api/products/123`) e o serviço recebe `/<recurso>/...`. Health: `/health/<serviço>`. Mapa completo em `tasks/T-004b-rotas-gateway.md`.
