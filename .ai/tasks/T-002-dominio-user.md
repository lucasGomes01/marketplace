# T-002 — Domínio User

- **Serviço:** Users (`Users.Domain`, `Users.UnitTests`)
- **Depende de:** T-001
- **Status:** done

## Objetivo
Modelar o usuário com as invariantes protegidas no domínio, sem dependência de banco.

## Critérios de aceite
- [x] `User.Register(email, name, role, passwordHash)` cria um usuário com `Id` (Guid v7) e `CreatedAt`.
- [x] O email é normalizado (trim + minúsculas) e validado; um email inválido lança `DomainException`.
- [x] Nome vazio ou só com espaços lança `DomainException`.
- [x] `Role` aceita apenas `Buyer` ou `Seller` (enum).
- [x] O domínio recebe a senha **já com hash**; o hashing fica numa interface `IPasswordHasher`
      (implementação com `PasswordHasher<T>` do ASP.NET Identity entra no T-003).

## Testes esperados
- Unit: um teste por critério acima (email válido e inválido table-driven com `[Theory]`).

## Fora de escopo
- Persistência, endpoints, JWT.

## Notas / decisões tomadas durante a tarefa
- `Register` recebe um `TimeProvider` como 5º parâmetro, para o `CreatedAt` ser testável (`FakeTimeProvider` nos testes).
- Email virou value object `Email` (record: igualdade por valor, máx. 254 caracteres, exige domínio com ponto).
- Regras extras além do pedido: nome com no máximo 100 caracteres; hash de senha obrigatório.
- `UserRole` começa em 1 (Buyer=1, Seller=2), para 0 nunca ser um papel válido por acidente.
- `IPasswordHasher` tem `Hash` e `Verify` (o `Verify` será usado no login, T-004).
- 20 testes unitários (EmailTests, UserTests).
