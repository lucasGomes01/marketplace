# T-003 — Cadastro de usuário (POST /users)

- **Serviço:** Users (`Users.Api`, `Users.IntegrationTests`)
- **Depende de:** T-002
- **Status:** todo

## Objetivo
Primeiro endpoint real, com banco de verdade. Estabelece o **padrão** de feature + EF Core + teste de integração
com Testcontainers que os outros serviços vão copiar.

## Critérios de aceite
- [ ] `POST /users { email, name, password, role }` → `201 Created` com `{ id, email, name, role }` e header `Location`.
- [ ] A senha é guardada com hash (nunca em texto puro) e não aparece na resposta.
- [ ] Email já cadastrado → `409 Conflict` com ProblemDetails.
- [ ] Dados inválidos (email ou nome) → `400` com ProblemDetails de validação.
- [ ] Senha com menos de 8 caracteres → `400`.
- [ ] `UsersDbContext` com índice único em `Email`; migration `InitialCreate` aplicada no startup em Development.

## Testes esperados
- Integração: `UsersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime`, que sobe um `MsSqlContainer`
  (Testcontainers), troca a connection string e aplica as migrations. Um teste por critério.
- Unit: se surgir lógica de validação fora do domínio.

## Fora de escopo
- Login/JWT (T-004), confirmação de email, recuperação de senha.

## Notas
- Este é o primeiro teste com Testcontainers: deixe a factory reutilizável e documente o padrão em `conventions.md`.
