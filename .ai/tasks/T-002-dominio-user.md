# T-002 — Domínio User

- **Serviço:** Users (`Users.Domain`, `Users.UnitTests`)
- **Depende de:** T-001
- **Status:** todo

## Objetivo
Modelar o usuário com as invariantes protegidas no domínio, sem dependência de banco.

## Critérios de aceite
- [ ] `User.Register(email, name, role, passwordHash)` cria um usuário com `Id` (Guid v7) e `CreatedAt`.
- [ ] O email é normalizado (trim + minúsculas) e validado; um email inválido lança `DomainException`.
- [ ] Nome vazio ou só com espaços lança `DomainException`.
- [ ] `Role` aceita apenas `Buyer` ou `Seller` (enum).
- [ ] O domínio recebe a senha **já com hash**; o hashing fica numa interface `IPasswordHasher`
      (implementação com `PasswordHasher<T>` do ASP.NET Identity entra no T-003).

## Testes esperados
- Unit: um teste por critério acima (email válido e inválido table-driven com `[Theory]`).

## Fora de escopo
- Persistência, endpoints, JWT.
