# CLAUDE.md

Antes de qualquer tarefa, leia `.ai/README.md` e siga o protocolo de trabalho descrito lá
(próxima tarefa em `.ai/tasks/BACKLOG.md`, TDD, atualizar `.ai/PROGRESS.md` ao terminar).

## Comandos
- Build .NET: `dotnet build`
- Testes .NET: `dotnet test` (os de integração precisam do Docker rodando)
- Testes Go: `cd src/services/Orders && go test ./...`
- Web: `cd src/web && npm run dev` / `npm test` / `npm run lint` / `npm run build`
- Infra: `docker compose up -d` · tudo: `docker compose --profile apps up -d --build`

## Portas locais
Gateway 5000 · Users 5001 · Catalog 5002 · Cart 5003 · Orders 5004 · Web 5173 · RabbitMQ UI 15672
