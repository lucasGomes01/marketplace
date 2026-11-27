# Progresso

> Mais recente no topo. Cada entrada: data, tarefa, o que foi feito, pendências/alertas.

## 2026-10-20 — T-028 Vitrine profissional
- Catalog: produto ganha categoria, marca, SKU, condição, fotos (URL), preço "de", peso/dimensões, garantia, frete grátis e ficha técnica (migration `ProductListingDetails`). `GET /products` com busca, filtros e ordenação.
- Web: cabeçalho com busca + barra de categorias + rodapé; vitrine com banner, categorias, filtros e ordenação; página do produto com galeria, ficha técnica e relacionados; anúncio em 5 seções com pré-visualização.
- Rebase sobre a T-027: a migration do Catalog foi refeita depois da `AddOutbox` (só colunas/índice de `Products`).

## 2026-10-20 — T-027 Outbox (ADR-005)
- Cart, Catalog e Orders gravam o evento em `OutboxMessages` **na mesma transação** do dado; um relay por serviço
  (lock `sp_getapplock`, conexão fora do pool) publica em lote e em ordem. `BuildingBlocks.Outbox` (.NET) e
  `internal/outbox` (Go). O checkout funciona com o Kafka fora; o "repita a requisição para republicar" acabou.
- Trace contínuo através do relay (traceparent gravado na linha). Painel "Outbox" (pendentes e idade).
- Carga `compra`: checkout ~2× mais rápido na mediana. O relay de um ack por vez fazia fila no Cart (31 eventos);
  em lote, ≤ 4. A cauda restante do "até confirmar" varia entre execuções e não vem do outbox.
- Alerta: migration gerada com mudança não commitada de outra tarefa na mesma pasta (refeita); regra em `conventions.md`.
  A T-028 (vitrine, outra sessão/worktree) vai precisar refazer a migration do Catalog depois do merge desta.

## 2026-10-19 — Fase 7: Envio (T-023 a T-026)
- Pedido confirmado ganha **um envio por vendedor**: `AwaitingPicking → Picked → Packed → Shipped → Delivered`. Vendedor avança até `Shipped` (`PUT /api/shipments/{orderId}/status`), comprador confirma "Recebi" (`Delivered`).
- `OrderShipments` no SQL Server com backfill idempotente dos pedidos antigos; gravação condicional (409 em requisições simultâneas). Evento `shipment.status-changed` (ainda sem consumidor).
- Telas: "Despachar" (`/vender/envios`) para vendedores; seção "Entrega" com linha do tempo no pedido; andamento em "Meus pedidos". E2E vai até "Entregue".
- Alerta local: Testcontainers no Windows precisa de `TESTCONTAINERS_RYUK_DISABLED=true`. Kafka UI (8080) conflita com outro container da máquina.
- Ideias novas: I-14 (cancelamento devolvendo estoque), I-15 (código de rastreio).

## 2026-10-16 — T-022 Observabilidade
- OpenTelemetry em .NET e Go, trace atravessando o Kafka (um checkout = um trace do gateway até a confirmação). Grafana em :3000 com histórico.
- Métricas de negócio (pedidos por status, tempo até resolver, reservas) e k6 no Prometheus com etiqueta de versão → painel compara experimentos de escala.
- Corrigido de passagem: vulnerabilidade alta em System.Security.Cryptography.Pkcs (transitiva do EF Core).

## 2026-10-16 — T-021 Gateway em Produção e cache da vitrine
- Compose em Production (migrações mantidas), sem log por requisição (YARP, EF Core). Output cache de 5 s no gateway só para leituras públicas do catálogo.
- Estresse: antes quebrava aos 1m58s; depois chega a 2000 req/s com p95 2,5 ms (melhor caso: poucas URLs, quase tudo do cache).

## 2026-10-14 — T-020 Testes de carga (k6)
- `load/`: 4 cenários em k6 via Docker na rede do compose. Navegação, compra e disputa passam nos limites; disputa prova 10 confirmados de 10 unidades com 100 compradores simultâneos.
- Estresse: degrada perto de ~800 req/s nesta máquina; gargalo é o Catalog (CPU), seguido do gateway e do SQL Server. Linha de base em `load/README.md`.
- Ideias que saem daqui: cache na vitrine (I-12), mais réplicas do Catalog, partições/consumidores para o atraso de confirmação sob carga.

## 2026-10-14 — T-016 E2E com Playwright no CI
- `e2e/`: o caminho feliz inteiro pelo navegador contra o compose (vender → comprar → Confirmado → Meus pedidos → estoque baixou). Job `e2e` no CI.
- O E2E achou 2 corridas reais no front (carrinho sobrescrito pela carga inicial; clique antes da sessão carregar) → corrigidas com testes. 75 testes de front.
- **MVP concluído (Fases 0–5).** Próximos passos são as ideias pós-MVP do BACKLOG.

## 2026-10-14 — T-015 Telas de pedido
- Carrinho com "Finalizar pedido" → `/pedidos/:id` acompanha o status (Processando → Confirmado | Recusado com motivo); "Meus pedidos" em `/pedidos` e no cabeçalho.
- 73 testes de front. Verificado no navegador contra o compose (confirmado e recusado).
- **Fase 4 concluída: o fluxo usuário → produto → carrinho → pedido funciona de ponta a ponta pelo navegador.** Próxima: **T-016** (E2E com Playwright no CI).

## 2026-10-14 — T-012 Estoque e confirmação do pedido
- Catalog consome `order.placed` (consumidor Kafka genérico novo em BuildingBlocks.Messaging) e baixa o estoque tudo ou nada, idempotente por `StockReservation`; publica `stock.reserved`/`stock.rejected` com motivo.
- Orders consome os dois e marca o pedido `Confirmed`/`Rejected`. Última unidade disputada: `rowversion` + nova tentativa → nunca estoque negativo.
- Verificado no compose ponta a ponta. **Fluxo usuário → produto → carrinho → pedido completo no backend.**
- Próxima: **T-015** (telas de checkout e "meus pedidos").

## 2026-10-13 — T-011 Orders consome o checkout (Go)
- Orders: consumidor Kafka (franz-go, grupo `orders-service`) → pedido no SQL Server (go-mssqldb) → `order.placed`; `GET /api/orders` e `/api/orders/{id}` com JWT validado em Go.
- Idempotência provada com replay do tópico. Teste de Kafka do Go: imagem `confluent-local:7.5.0` (a 7.9.0 quebrava o módulo — diagnóstico inicial "limitação do Docker Desktop" estava errado).
- Próxima: **T-012** (Catalog baixa estoque → `stock.reserved`/`stock.rejected` → Orders confirma/recusa).

## 2026-10-13 — T-010 Checkout no Kafka
- `POST /api/cart/checkout` publica `cart.checked-out` (checkoutId determinístico = id do pedido) e esvazia o carrinho; 503 se o Kafka cair.
- `IEventPublisher` (porta) + `KafkaEventPublisher` (BuildingBlocks.Messaging). Bug de ordem dos itens corrigido (`Position`).
- ⚠️ I-10: instabilidade intermitente nos testes de integração em paralelo, não reproduzida. Próxima: **T-011** (Orders consome em Go).

## 2026-10-13 — T-009 Domínio do pedido (Go)
- `internal/order`: Order, Item, Money (centavos), status Pending→Confirmed|Rejected com transições idempotentes, UUID v7. 23 testes.
- Próxima: **T-010** (BuildingBlocks.Messaging com Kafka + checkout publicando `cart.checked-out`).

## 2026-10-13 — T-014b Telas do carrinho
- "Adicionar ao carrinho" na página do produto, tela `/carrinho` (± quantidade, remover, total) e contador no cabeçalho.
- **Fase 3 (carrinho) concluída.** Próxima: **Fase 4 — pedido**: T-009 (domínio Order em Go) → T-010 (checkout publica no Kafka) → T-011 → T-012 → T-015.

## 2026-10-13 — T-008 API do carrinho
- `GET/POST/PUT/DELETE /api/cart...` em camadas; preço/estoque vêm do Catalog por HTTP; 503 se o Catalog cair; rowversion.
- Bug do EF com coleção owned (item novo rastreado como Modified) corrigido no repositório. 231 testes .NET verdes.
- Próxima: **T-014b** (botão "Adicionar ao carrinho" e tela do carrinho).

## 2026-10-13 — T-007 Domínio do carrinho
- `ShoppingCart` + `CartItem` (snapshot do produto) + `Money` com soma. Vários vendedores, uma moeda, 1..99 por item. 23 testes.

## 2026-10-13 — T-014 Telas do catálogo
- Vitrine (`/`, paginada), página do produto (`/produtos/:id`) e anunciar produto (`/vender/novo`, com convite para quem só compra).
- Preço em formato brasileiro na entrada e na exibição. Layout corrigido para celular. 49 testes de front.
- Próximas: **T-007** (domínio do carrinho) → **T-008** (API do carrinho) → **T-014b** (botão e tela do carrinho).

## 2026-10-13 — T-006 API de produtos
- Catalog em camadas: `POST /api/products` (Seller), `GET /api/products` (paginado, mais recentes primeiro), `GET /api/products/{id}`.
- Bug corrigido em todos os serviços: JSON malformado dava 500 → agora 400 (`UseMarketplaceExceptionHandler`). 170 testes .NET verdes.
- Próxima: **T-014** (telas do catálogo).

## 2026-10-13 — T-019 Conta única
- Toda conta compra; "Começar a vender" (`POST /api/users/me/seller`) ativa o perfil de vendedor e devolve token novo.
- Token com um claim `role` por papel. Migration converte `Role=Seller` → `IsSeller` (testada). Cadastro sem tipo de conta.
- Próxima: **T-006** (Catalog API em camadas; `POST /products` exige policy `Seller`).

## 2026-10-13 — T-018 Camadas SOLID
- Users agora em Domain / Application / Infrastructure / Api (ADR-004). Casos de uso = `IHandler<,>` → `Result<T>`; portas em `Abstractions/`.
- Novos `BuildingBlocks.Application` e `BuildingBlocks.Api`; testes de arquitetura garantem a regra de dependência.
- Passo a passo "caso de uso novo" em `conventions.md`. **Catalog (T-006) nasce nesse padrão.** 126 testes .NET + 22 front verdes.

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
