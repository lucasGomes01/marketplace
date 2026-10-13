# T-004b — Rotas do gateway por recurso (resolve I-07)

- **Serviço:** Gateway
- **Depende de:** T-004
- **Status:** done

## Objetivo
Acabar com URLs como `/api/users/users`. O gateway passa a rotear por **recurso** (não por nome de serviço) e só
remove o prefixo `/api`. Cada serviço continua expondo os recursos na raiz (`/users`, `/products`...).

## Mapa de rotas
| Rota pública | Serviço | Caminho no serviço |
|---|---|---|
| `/api/users/**` | Users | `/users/**` |
| `/api/auth/**` | Users | `/auth/**` |
| `/api/products/**` | Catalog | `/products/**` |
| `/api/cart/**` | Cart | `/cart/**` |
| `/api/orders/**` | Orders | `/orders/**` |
| `/health/{serviço}` | cada um | `/health` |
| `/health` | o próprio gateway | — |

## Critérios de aceite
- [x] Cada rota da tabela chega ao serviço certo, com o caminho certo (incluindo query string).
- [x] `/api/users` (sem nada depois) também é roteado.
- [x] Uma rota desconhecida (`/api/nada`) → `404` no gateway.
- [x] `/health` responde pelo próprio gateway.

## Testes esperados
- Integração (`tests/Gateway/Gateway.IntegrationTests`): a factory do gateway aponta todos os clusters para um
  servidor "eco" local, que devolve qual serviço recebeu a chamada e o caminho recebido.

## Fora de escopo
- Validar JWT ou aplicar rate limit no gateway.

## Notas
- Regra para serviços novos: o recurso **é** o prefixo. Novo recurso → nova rota no `appsettings.json` do gateway.
- 2026-10-13: 13 testes de roteamento com servidor eco. Validado no compose: `POST /api/users` 201, `/api/auth/login`, `/api/users/me` 200, `/health/<serviço>` 200; a rota antiga `/api/users/users` agora dá 404.
