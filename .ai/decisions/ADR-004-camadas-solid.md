# ADR-004 — Camadas por serviço guiadas por SOLID

- **Data:** 2026-10-13
- **Status:** aceita

## Contexto
O dono do projeto pediu SOLID. Até a T-013 cada serviço .NET tinha só `Domain` + `Api`, e os endpoints faziam
tudo: validação, regra de aplicação, acesso ao `DbContext`, emissão de token e tradução para HTTP.
Exemplo: `RegisterUser.Handle` validava senha, chamava o domínio, consultava o EF e tratava `SqlException`.

## Decisão
Cada serviço .NET passa a ter 4 projetos, com dependências apontando **só para dentro**:

```
Api ──► Application ──► Domain
 │           ▲
 └──► Infrastructure (implementa as interfaces da Application)
```

| Projeto | Contém | Pode depender de |
|---|---|---|
| `X.Domain` | Entidades, value objects, regras | nada (só BCL) |
| `X.Application` | Casos de uso (handlers), **portas** (interfaces: repositórios, hasher, emissor de token, publicador de eventos), DTOs | Domain, `BuildingBlocks.Application` |
| `X.Infrastructure` | EF Core, migrations, repositórios, JWT, Kafka, hashing | Application, Domain, BuildingBlocks |
| `X.Api` | Endpoints finos (HTTP ↔ caso de uso), composição (DI) | todos |

Peças compartilhadas:
- `BuildingBlocks.Application`: `Result<T>`, `Error` (com **código**, tipo e campo), `IHandler<TRequest, TResponse>`
  e registro automático dos handlers.
- `BuildingBlocks.Api`: `result.ToHttpResult()` traduz `Error` em `ValidationProblem` (400), 401, 404, 409.

Como cada princípio aparece:
- **S** — endpoint só traduz HTTP; handler só orquestra o caso de uso; repositório só persiste; a entidade só
  protege invariantes.
- **O** — novo caso de uso = nova classe handler (registrada por varredura); novo tipo de erro = uma linha no mapeamento
  HTTP. Nada existente precisa mudar. Decorators (log, métricas) podem envolver `IHandler<,>` sem tocar nos handlers.
- **L** — qualquer implementação de uma porta (EF ou fake em memória nos testes) é intercambiável.
- **I** — portas pequenas e específicas (`IPasswordHasher`, `ITokenIssuer`, `IUserRepository`), não um "IUserService" gigante.
- **D** — Application define as interfaces; Infrastructure as implementa; a Api é a única que conhece as duas
  (composition root).

Falhas **esperadas** (validação, conflito, credencial inválida) viram `Result` com `Error`, não exceções.
Exceções ficam para o inesperado. Os códigos de erro (`password.too_short`...) preparam a ideia I-09.

A regra de dependência é verificada por **testes de arquitetura** (referências entre assemblies) em cada serviço.

## Consequências
- (+) Casos de uso testáveis com fakes, sem banco nem HTTP (testes rápidos).
- (+) Trocar infraestrutura (ex.: RabbitMQ → Kafka) não toca em Application/Domain.
- (−) Mais projetos e mais arquivos por funcionalidade; mais cerimônia para operações triviais.
- (−) Mapeamentos extras (request → command → DTO → response).
