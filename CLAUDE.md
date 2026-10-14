# CLAUDE.md

Antes de qualquer tarefa, leia `.ai/README.md` e siga o protocolo de trabalho descrito lá
(próxima tarefa em `.ai/tasks/BACKLOG.md`, TDD, atualizar `.ai/PROGRESS.md` ao terminar).

## Comandos
- Build .NET: `dotnet build`
- Testes .NET: `dotnet test` (os de integração precisam do Docker rodando)
- Testes Go: `cd src/services/Orders && go test ./...` — sem Go instalado (máquina do dono):
  unitários: `MSYS_NO_PATHCONV=1 docker run --rm -v "C:/marketplace/src/services/Orders:/src" -v gomodcache:/go/pkg/mod -w /src golang:1.26 sh -c "gofmt -l . && go vet ./... && go test -short -race ./..."`
  integração (testcontainers-go): adicione `-v //var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e TESTCONTAINERS_RYUK_DISABLED=true` e tire o `-short`.
  Kafka nos testes Go: use `confluentinc/confluent-local:7.5.0` (a única imagem que o módulo testcontainers-go v0.44 suporta).
- Web: `cd src/web && npm run dev` / `npm test` / `npm run lint` / `npm run build`
- E2E (Playwright, precisa do compose com `--profile apps` no ar): `cd e2e && npm ci && npx playwright install chromium && npm run wait-for-stack && npm test`
- Infra: `docker compose up -d` · tudo: `docker compose --profile apps up -d --build`

## Portas locais
Gateway 5000 · Users 5001 · Catalog 5002 · Cart 5003 · Orders 5004 · Web 5173 · Kafka 9092 (localhost) · Kafka UI 8080
