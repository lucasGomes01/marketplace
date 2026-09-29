# T-004 — Login com JWT e autenticação compartilhada

- **Serviço:** Users + nova lib `BuildingBlocks.Auth`
- **Depende de:** T-003
- **Status:** done

## Objetivo
Permitir que a pessoa faça login e receba um JWT, e dar a **todos** os serviços uma forma única de validar esse
token (mesmo issuer, audience, chave e nomes de claims), para Catalog e Cart só precisarem de uma linha no `Program.cs`.

## Critérios de aceite
### BuildingBlocks.Auth
- [x] `JwtOptions` (seção `Jwt`: `Issuer`, `Audience`, `SigningKey`, `ExpirationMinutes`) validada no startup;
      chave com menos de 32 caracteres é rejeitada (HS256 exige 256 bits).
- [x] `JwtTokenIssuer.Issue(userId, email, role)` gera um token HS256 com claims `sub`, `email`, `role`
      e expiração de `ExpirationMinutes` (relógio via `TimeProvider`).
- [x] Token expirado, com assinatura de outra chave ou de outro issuer é rejeitado.
- [x] `services.AddMarketplaceJwtAuth(configuration)` configura JwtBearer com esses parâmetros, sem o mapeamento
      legado de claims, e cria as policies `Buyer` e `Seller`.
- [x] `ClaimsPrincipal.GetUserId()` lê o `sub`.

### Users
- [x] `POST /auth/login { email, password }` → `200 { accessToken, tokenType: "Bearer", expiresAt }`.
- [x] Email inexistente ou senha errada → `401` com **a mesma** resposta (não revela se o email existe).
- [x] Email ou senha em branco → `400`.
- [x] O email do login é normalizado (`Maria@X.com` entra como `maria@x.com`).
- [x] `GET /users/me` com token válido → `200 { id, email, name, role }`; sem token ou com token adulterado → `401`.

## Testes esperados
- Unit (`tests/BuildingBlocks/BuildingBlocks.Auth.UnitTests`): emissão, claims, expiração, chave e issuer errados,
  validação das options.
- Integração (`Users.IntegrationTests`): login feliz, 401 (email inexistente e senha errada com o mesmo corpo),
  400, `/users/me` com e sem token.

## Fora de escopo
- Refresh token, logout e revogação.
- Validar o JWT no gateway (o gateway só repassa; cada serviço valida).
- Proteger endpoints do Catalog e do Cart (entra em T-006 e T-008).
- Rate limit de tentativas de login (virar ideia).

## Notas / decisões tomadas durante a tarefa
- Emissão e validação ficam na mesma lib, para os parâmetros nunca divergirem entre quem emite e quem valida.
- Com email inexistente, o login ainda executa uma verificação de hash "falsa", para o tempo de resposta não
  revelar se o email existe (timing attack).
- Email com formato inválido no login → `401` (credencial inválida), não `400`, para não dar pistas.
- `GET /users/me` com token válido mas usuário apagado → `404`.
- Token com `jti` (id único) desde já, para facilitar uma futura revogação.
- `/users/me` é o primeiro endpoint protegido e prova a lib; Catalog e Cart passam a usá-la no T-006 e T-008.
- Chave JWT: `appsettings.Development.json` (execução local) e `JWT_SIGNING_KEY` no `.env` (compose).
  Issuer e audience ficam em `appsettings.json` e no anchor `x-dotnet-service` do compose.
- Testes: 8 unitários (lib) + 9 de integração novos (18 no total no Users). Validado via compose/gateway:
  cadastro 201 → login → `/users/me` 200; sem token 401; senha errada 401.
