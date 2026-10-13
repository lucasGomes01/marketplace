# T-003 — Cadastro de usuário (POST /users)

- **Serviço:** Users (`Users.Api`, `Users.IntegrationTests`)
- **Depende de:** T-002
- **Status:** done

## Objetivo
Primeiro endpoint real, com banco de verdade. Estabelece o **padrão** de feature + EF Core + teste de integração
com Testcontainers que os outros serviços vão copiar.

## Critérios de aceite
- [x] `POST /users { email, name, password, role }` → `201 Created` com `{ id, email, name, role }` e header `Location`.
- [x] A senha é guardada com hash (nunca em texto puro) e não aparece na resposta.
- [x] Email já cadastrado → `409 Conflict` com ProblemDetails.
- [x] Dados inválidos (email ou nome) → `400` com ProblemDetails de validação.
- [x] Senha com menos de 8 caracteres → `400`.
- [x] `UsersDbContext` com índice único em `Email`; migration `InitialCreate` aplicada no startup em Development.

## Testes esperados
- Integração: `UsersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime`, que sobe um `MsSqlContainer`
  (Testcontainers), troca a connection string e aplica as migrations. Um teste por critério.
- Unit: se surgir lógica de validação fora do domínio.

## Fora de escopo
- Login/JWT (T-004), confirmação de email, recuperação de senha.

## Notas
- Este é o primeiro teste com Testcontainers: deixe a factory reutilizável e documente o padrão em `conventions.md`.
- 2026-09-29 (concluída):
  - Padrões de teste e de endpoint documentados em `context/conventions.md`.
  - `DomainException` ganhou `Field`, para a API devolver erro por campo sem duplicar as regras (+1 teste unitário).
  - Validações da API (senha ≥ 8 e papel) retornam antes das do domínio; por isso, erros de email/nome só
    aparecem depois que senha e papel estiverem ok. Aceitável no MVP.
  - `Location: /users/{id}` aponta para um `GET /users/{id}` que ainda não existe (entra quando for necessário).
  - Hash com `PasswordHasher` do ASP.NET Identity (PBKDF2), que já vem no shared framework (sem pacote extra).
  - `dotnet-ef` instalado como ferramenta local (`dotnet-tools.json`): rode `dotnet tool restore`.
  - ⚠️ Pelo gateway a URL fica `/api/users/users` (o gateway remove `/api/users`). Resolvido na T-004b: agora é `/api/users`.
  - Validado também via `docker compose`: 201, 409 (email duplicado com outra caixa) e 400.
