# Progresso

> Mais recente no topo. Cada entrada: data, tarefa, o que foi feito, pendências/alertas.

## 2026-10-13 — T-017 Kafka
- RabbitMQ → Kafka 4.1 (KRaft). Tópicos declarados no `kafka-init`, auto-create desligado, Kafka UI em :8080. ADR-003.
- Tópico `cart.checked-out` (com hífen). `BuildingBlocks.Messaging` agora referencia Confluent.Kafka. Próxima: **T-018** (camadas SOLID).

## 2026-10-13 — T-013 Telas de conta (front)
- Backlog reorganizado: cada fase entrega backend **e** telas (T-014 após T-006, nova T-014b após T-008, T-015 após T-012).
- Front com react-router, cliente HTTP tipado, sessão com JWT, telas Cadastro/Entrar/Minha conta + layout responsivo claro/escuro.
- 22 testes de front (Vitest + Testing Library + MSW) no CI. Verificado no navegador contra a API real. Próxima: **T-006** (API de produtos) e depois **T-014** (telas de produtos).

## 2026-10-13 — T-005 Domínio Product
- `Product` (criar, mudar preço, entrada/saída de estoque, dono) + value object `Money` em `Catalog.Domain`; 34 testes.
- Nicho segue genérico. 96 testes verdes na solution. Próxima: **T-006** (endpoints do Catalog: copiar o padrão do Users).

## 2026-10-13 — T-004b Rotas do gateway
- Gateway roteia por recurso: `/api/users`, `/api/auth`, `/api/products`, `/api/cart`, `/api/orders`; health em `/health/<serviço>`.
- Novo projeto `Gateway.IntegrationTests` (13 testes). 62 testes verdes na solution. Próxima: **T-005** (Product, nicho genérico).

## 2026-10-13 — T-001b GitHub + CI
- Repo público https://github.com/lucasGomes01/marketplace; primeiro CI verde nos 4 jobs (.NET, Go, Web, Docker).
- Histórico reescrito para remover `Co-Authored-By` de IA: commits só no nome do dono (regra em `conventions.md`).
- `gh` instalado e autenticado (lucasGomes01). Pendentes antes da T-005: nicho e I-07 (rotas do gateway).

## 2026-09-29 — T-004 Login + JWT
- Nova lib `BuildingBlocks.Auth` (emissão + validação + policies Buyer/Seller) e `POST /auth/login` + `GET /users/me`.
- Login não revela se o email existe (mesmo 401 e mesmo custo de hash). 49 testes verdes na solution.
- Chave JWT em `JWT_SIGNING_KEY` (.env). Próximas: **T-005** (domínio Product) e **T-009** (Order em Go), ambas desbloqueadas.
  ⚠️ Decidir a I-07 (rotas do gateway) e o nicho antes de T-005/T-006.

## 2026-09-29 — T-003 POST /users
- Cadastro com EF Core + migration `InitialCreate` + 9 testes de integração com Testcontainers (SQL Server real).
- Padrões de endpoint e de teste de integração documentados em `context/conventions.md`: **copie-os nos outros serviços**.
- Próxima: **T-004** (login + JWT). Alerta: rota `/api/users/users` no gateway (ideia I-07).

## 2026-09-29 — T-002 Domínio User
- `User.Register` + value object `Email` + `UserRole` + `IPasswordHasher` em `Users.Domain`; 20 testes unitários verdes.
- `CreatedAt` vem de um `TimeProvider` injetado. Próxima: **T-003** (POST /users com EF Core + Testcontainers).

## 2026-09-29 — T-001 Ambiente validado
- Compose (infra + apps) ok; os 4 serviços respondem 200 via gateway. Go testado via `golang:1.26-alpine`.
- Identidade git local e `.gitattributes` (LF). **Pendente com o humano: T-001b** (repo no GitHub + CI).

## 2026-09-29 — T-000 Fundação
- Solution .NET 10 (`Marketplace.slnx`) com Users, Catalog, Cart (Api + Domain + UnitTests + IntegrationTests),
  Gateway YARP e BuildingBlocks.Messaging. Health check + teste de fumaça em cada API (verdes).
- Orders em Go (stdlib, `GET /health` + teste). **Go não está instalado na máquina do dev** — testar via
  Docker ou instalar Go 1.26.
- Front React + TS + Vite em `src/web` com proxy `/api` → gateway.
- `docker-compose.yml` (infra por padrão, apps no profile `apps`), `Dockerfile.dotnet`, CI no GitHub Actions.
- Pendente: validar `docker compose up` (Docker Desktop estava desligado).
