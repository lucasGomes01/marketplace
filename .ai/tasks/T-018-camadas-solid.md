# T-018 — SOLID: camadas no serviço Users

- **Serviço:** Users + `BuildingBlocks.Application` + `BuildingBlocks.Api`
- **Depende de:** T-017
- **Status:** done

## Objetivo
Aplicar o ADR-004 no Users **sem mudar nenhum comportamento externo**: os 18 testes de integração (HTTP + SQL Server
real) e os 22 testes do front continuam passando sem alteração de asserções.

## Critérios de aceite
- [x] Projetos `Users.Application` e `Users.Infrastructure` criados; `Users.Api` só com endpoints e composição.
- [x] `BuildingBlocks.Application` (`Result`, `Error`, `ErrorType`, `IHandler<,>`, `AddHandlers`) e
      `BuildingBlocks.Api` (`ToHttpResult`) com testes unitários.
- [x] Casos de uso `RegisterUserHandler`, `LoginHandler`, `GetUserByIdHandler` com testes unitários usando fakes
      (sem banco, sem HTTP).
- [x] Portas: `IUserRepository`, `IPasswordHasher` (sai do Domain), `ITokenIssuer`. Implementações na Infrastructure.
- [x] `UsersDbContext`, configuração e migrations movidos para `Users.Infrastructure/Persistence` (mesmo id de
      migration: bancos existentes continuam válidos).
- [x] Testes de arquitetura: Domain não referencia nada do projeto nem EF/ASP.NET; Application não referencia
      Infrastructure, EF Core nem ASP.NET; Infrastructure não referencia Api.
- [x] Testes de integração do Users passam **sem mudar nenhuma asserção** (só `using`/namespaces).
- [x] `conventions.md` e `architecture.md` atualizados com a estrutura e um passo a passo de "novo caso de uso".

## Fora de escopo
- Catalog e Cart (nascem no padrão novo: T-006 e T-008).
- Expor os códigos de erro na API (I-09).

## Notas
- 2026-10-13 (concluída). Testes: BuildingBlocks.UnitTests 12 (Result, Error→HTTP, registro de handlers); Users.UnitTests 21 → 39 (handlers com fakes + 4 de arquitetura). Os 18 de integração passaram mudando **só 2 linhas de `using`**, nenhuma asserção.
- Referências reais conferidas: Domain → só System.*; Application → Domain + BuildingBlocks.Application + DI.Abstractions; EF/SqlClient só na Infrastructure.
- `IPasswordHasher` saiu do Domain para `Application/Abstractions` (é porta, não regra de entidade).
- Migrations movidas com `git mv` para `Users.Infrastructure/Persistence/Migrations` (mesmo id; `has-pending-model-changes` = nenhuma).
- Efeito colateral bom: ProblemDetails de 401/404/409 agora trazem `code` (ex.: `email.already_registered`). Validação (400) ainda só com mensagens: completar na I-09.
- `AddUsersInfrastructure` depende de `AddMarketplaceJwtAuth` (registra o `JwtTokenIssuer`) — documentado no método.
- Validado no compose via gateway: 201, 409 com code, 400 por campo, login, /users/me, 401 com code. Front: 22 testes verdes sem mudança.
