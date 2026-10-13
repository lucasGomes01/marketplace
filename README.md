# Marketplace

Marketplace multi-vendedor em microsserviços: **.NET 10**, **Go**, **React + TypeScript**, **SQL Server**,
**Kafka**, **YARP**, **Docker**. Desenvolvido com TDD e de forma incremental com apoio de IA (ver [`.ai/`](.ai/README.md)).

## Arquitetura
Veja [`.ai/context/architecture.md`](.ai/context/architecture.md).

| Serviço | Stack | Porta |
|---|---|---|
| Gateway | .NET + YARP | 5000 |
| Users | .NET + EF Core | 5001 |
| Catalog | .NET + EF Core | 5002 |
| Cart | .NET + EF Core | 5003 |
| Orders | Go | 5004 |
| Web | React + Vite | 5173 |

## Rodando localmente
```bash
cp .env.example .env
docker compose up -d                           # SQL Server + Kafka (UI em :8080)
dotnet test                                    # testes .NET (precisa do Docker)
cd src/web && npm install && npm run dev       # front
```

Tudo em containers:
```bash
docker compose --profile apps up -d --build
curl http://localhost:5000/health/catalog
```

## Roadmap
[`.ai/tasks/BACKLOG.md`](.ai/tasks/BACKLOG.md). Objetivo do MVP: **usuário → produto → carrinho → pedido**.
