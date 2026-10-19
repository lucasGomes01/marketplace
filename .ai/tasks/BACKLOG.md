# Backlog

Status: `todo` · `doing` · `done` · `idea` (não priorizado). A IA pega a **primeira `todo`** com dependências `done`.
Tarefas sem arquivo próprio: criar a partir de `_template.md` antes de começar (etapa de planejamento).

> **Ordem intercalada (decidida em 2026-10-13):** cada fase entrega o backend **e** as telas dela, para o projeto
> poder ser acompanhado pelo navegador. Por isso as tarefas de front (T-013..T-015) aparecem dentro das fases.

## Fase 0 — Fundação
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-000 | Scaffold: solution, gateway, serviços, web, compose, CI, `.ai` | Infra | — | done |
| T-001 | Validar ambiente local: `docker compose up`, testes Go via Docker | Infra | T-000 | done |
| T-001b | Publicar no GitHub e ver CI verde | Infra | T-001 | done |

## Fase 1 — Usuário
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-002 | Domínio `User` (email válido, papel Buyer/Seller, hash de senha) | Users | T-001 | done |
| T-003 | `POST /users` (cadastro) com EF Core + migration + teste de integração com Testcontainers | Users | T-002 | done |
| T-004 | `POST /auth/login` → JWT; lib de validação JWT compartilhada entre os serviços | Users | T-003 | done |
| T-004b | Rotas do gateway por recurso (`/api/users`, `/api/auth`, `/api/products`...) — resolve I-07 | Gateway | T-004 | done |
| T-013 | **Telas:** setup Vitest + Testing Library + MSW, cliente HTTP tipado, rotas; cadastro, login e "minha conta" | Web | T-004b | done |

## Mudanças de arquitetura (2026-10-13)
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-017 | Trocar RabbitMQ por Kafka (KRaft, tópicos declarados, Kafka UI) — ADR-003 | Infra | T-013 | done |
| T-018 | SOLID: camadas Domain/Application/Infrastructure/Api, começando pelo Users — ADR-004 | Users | T-017 | done |
| T-019 | Conta única: toda conta compra; "Começar a vender" ativa o perfil de vendedor na mesma conta | Users + Web | T-018 | done |

## Fase 2 — Produto
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-005 | Domínio `Product` + value object `Money` (preço > 0, estoque ≥ 0, dono Seller) | Catalog | T-001 | done |
| T-006 | `POST /products` (só Seller), `GET /products` (paginado), `GET /products/{id}` — já nas camadas do ADR-004 (criar `Catalog.Application`/`Catalog.Infrastructure`) | Catalog | T-004, T-005, T-019 | done |
| T-014 | **Telas:** lista e detalhe de produtos; cadastro de produto (Seller). "Adicionar ao carrinho" entra na T-014b | Web | T-006, T-013 | done |

## Fase 3 — Carrinho
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-007 | Domínio `Cart` (adicionar, somar quantidade, remover, total) | Cart | T-005 | done |
| T-008 | API do carrinho (`GET /cart`, `POST /cart/items`, `DELETE /cart/items/{productId}`); consulta preço/estoque no Catalog por HTTP | Cart | T-006, T-007 | done |
| T-014b | **Telas:** botão "adicionar ao carrinho" e tela do carrinho (sem checkout) | Web | T-008, T-014 | done |

## Fase 4 — Pedido
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-009 | Domínio `Order` em Go + máquina de status (Pending → Confirmed/Rejected) | Orders | T-001 | done |
| T-010 | BuildingBlocks.Messaging (producer/consumer Kafka + envelope, `IEventPublisher`) e `POST /cart/checkout` publicando `cart.checked-out` | Cart | T-008 | done |
| T-011 | Orders consome `cart.checked-out` (franz-go), persiste no SQL Server, publica `order.placed`; `GET /orders` do comprador | Orders | T-009, T-010 | done |
| T-012 | Catalog baixa estoque em `order.placed` (idempotente) → `stock.reserved`/`stock.rejected`; Orders atualiza status (o carrinho já é limpo no checkout, T-010) | Catalog/Orders/Cart | T-011 | done |
| T-015 | **Telas:** finalizar pedido (checkout) e "meus pedidos" com status | Web | T-012, T-014b | done |

## Fase 5 — Qualidade do fluxo completo
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-016 | Teste E2E do caminho feliz (Playwright) rodando no CI com `docker compose` | Infra | T-015 | done |

## Fase 6 — Pós-MVP
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-020 | Testes de carga com k6 (navegação, compra, disputa de estoque, estresse) + linha de base | Infra | T-016 | done |
| T-021 | Gateway: modo Produção (sem log por requisição) e cache de 5 s na vitrine | Gateway + Infra | T-020 | done |
| T-022 | Observabilidade: OpenTelemetry (.NET e Go, trace atravessando o Kafka) + Grafana com comparação de testes de carga | Todos + Infra | T-021 | done |

## Fase 7 — Envio (decidida em 2026-10-19)
Depois de `Confirmed`, cada **vendedor** tem um **envio** com os itens dele:
`AwaitingPicking → Picked (separado) → Packed (embalado) → Shipped (enviado) → Delivered (entregue)`.
O vendedor avança até `Shipped`; o **comprador** confirma `Delivered` ("Recebi").

| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-023 | Domínio `Shipment` em Go: um envio por vendedor, máquina de status, quem pode avançar | Orders | T-012 | done |
| T-024 | API (`/shipments` do vendedor, "Recebi" do comprador), persistência + backfill, evento `shipment.status-changed`, rota no gateway | Orders + Gateway + Infra | T-023 | done |
| T-025 | **Telas:** "Pedidos a despachar" (vendedor) e linha do tempo + "Recebi" em "meus pedidos" (comprador) | Web | T-024 | doing |
| T-026 | E2E estendido: compra → separado → embalado → enviado → recebido | Infra | T-025 | todo |

## Ideias (pós-MVP)
| ID | Ideia | Status |
|---|---|---|
| I-01 | Outbox pattern para publicar eventos de forma transacional | idea |
| I-02 | Pagamento em modo teste (Stripe/Mercado Pago) com split para o vendedor | idea |
| I-03 | Painel do vendedor (vendas, estoque) | idea |
| I-04 | Avaliações de comprador ↔ vendedor | idea |
| I-05 | Observabilidade: OpenTelemetry + trace distribuído entre .NET e Go | done (T-022) |
| I-06 | CD: publicar imagens no GHCR e deploy automático | idea |
| I-07 | Rotas do gateway: evitar `/api/users/users` → virou **T-004b** | done |
| I-08 | Rate limit nas tentativas de login (no gateway ou no Users) + refresh token | idea |
| I-11 | Dead-letter topic (`<tópico>.dlq`) para eventos inválidos, em vez de só registrar no log e pular | idea |
| I-10 | Investigar falha intermitente dos testes de integração com a suíte inteira em paralelo (vários SQL Server + Kafka). Visto 2× em 2026-10-13, não reproduzido em 6 rodadas seguidas. O CI agora guarda os resultados detalhados quando falha | idea |
| I-12 | Cache da vitrine (`GET /products`) — o teste de estresse (T-020) mostrou o Catalog como primeiro gargalo | done (T-021, em memória no gateway) |
| I-13 | Imagens de produto: MinIO no compose (API S3; em produção S3 privado + CloudFront com OAC), upload direto do browser via presigned URL gerada pelo Catalog, `ImageKey` no `Product` (só a referência, nunca o binário), chaves imutáveis (`products/{id}/{hash}.webp`) com `Cache-Control: public, max-age=31536000, immutable`, thumbnails para a listagem e exibição no web. Imagem **não** passa pelo Gateway | idea |
| I-14 | Cancelamento do pedido/envio antes de `Shipped`, devolvendo o estoque no Catalog (evento) | idea |
| I-15 | Código de rastreio da transportadora no envio `Shipped` | idea |
| I-09 | Erros com **códigos** para o front traduzir sem regex. Já existem no `Result` (T-018) e saem em 401/404/409; falta expô-los no 400 de validação e usar no front | idea |
