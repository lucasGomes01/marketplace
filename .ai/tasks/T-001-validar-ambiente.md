# T-001 — Validar ambiente local e CI

- **Serviço:** Infra
- **Depende de:** T-000
- **Status:** todo

## Objetivo
Garantir que qualquer pessoa (ou IA) consiga subir e testar tudo antes de começar as features.

## Critérios de aceite
- [ ] `docker compose up -d` sobe o SQL Server e o RabbitMQ com healthcheck `healthy`.
- [ ] `docker compose --profile apps up -d --build` sobe todos os serviços; `GET http://localhost:5000/api/orders/health`
      e as rotas equivalentes de users, catalog e cart respondem 200 pelo gateway.
- [ ] Testes do Go rodam localmente (Go 1.26 instalado **ou** via
      `docker run --rm -v ${PWD}/src/services/Orders:/src -w /src golang:1.26-alpine go test ./...`).
- [ ] Repositório publicado no GitHub e o workflow `CI` verde nos 4 jobs.

## Testes esperados
- Os testes de fumaça já existentes (health) de todos os serviços.

## Fora de escopo
- Qualquer regra de negócio.

## Notas
- Se a imagem `golang:1.26-alpine` não existir, ajuste a versão no `go.mod` e no `Dockerfile` do Orders.
