# T-001 — Validar ambiente local e CI

- **Serviço:** Infra
- **Depende de:** T-000
- **Status:** done

## Objetivo
Garantir que qualquer pessoa (ou IA) consiga subir e testar tudo antes de começar as features.

## Critérios de aceite
- [x] `docker compose up -d` sobe o SQL Server e o RabbitMQ com healthcheck `healthy`.
- [x] `docker compose --profile apps up -d --build` sobe todos os serviços; `GET http://localhost:5000/api/orders/health`
      e as rotas equivalentes de users, catalog e cart respondem 200 pelo gateway.
- [x] Testes do Go rodam localmente (Go 1.26 instalado **ou** via
      `docker run --rm -v ${PWD}/src/services/Orders:/src -w /src golang:1.26-alpine go test ./...`).
- [x] Repositório publicado no GitHub e CI verde (T-001b, 2026-10-13): https://github.com/lucasGomes01/marketplace

## Testes esperados
- Os testes de fumaça já existentes (health) de todos os serviços.

## Fora de escopo
- Qualquer regra de negócio.

## Notas
- 2026-09-29: validado localmente. Os 4 serviços respondem 200 via gateway; Go ok via `golang:1.26-alpine` (gofmt, vet, test).
- Adicionado `.gitattributes` (LF) e identidade git local do repositório.
- Se a imagem `golang:1.26-alpine` não existir, ajuste a versão no `go.mod` e no `Dockerfile` do Orders.
