# Backlog

Status: `todo` · `doing` · `done` · `idea` (não priorizado). A IA pega a **primeira `todo`** com dependências `done`.
Tarefas sem arquivo próprio: criar a partir de `_template.md` antes de começar (etapa de planejamento).

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

## Fase 2 — Produto
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-005 | Domínio `Product` + value object `Money` (preço > 0, estoque ≥ 0, dono Seller) | Catalog | T-001 | todo |
| T-006 | `POST /products` (só Seller), `GET /products` (paginado), `GET /products/{id}` | Catalog | T-004, T-005 | todo |

## Fase 3 — Carrinho
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-007 | Domínio `Cart` (adicionar, somar quantidade, remover, total) | Cart | T-005 | todo |
| T-008 | API do carrinho (`GET /cart`, `POST /cart/items`, `DELETE /cart/items/{productId}`); consulta preço/estoque no Catalog por HTTP | Cart | T-006, T-007 | todo |

## Fase 4 — Pedido
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-009 | Domínio `Order` em Go + máquina de status (Pending → Confirmed/Rejected) | Orders | T-001 | todo |
| T-010 | BuildingBlocks.Messaging (publisher/consumer RabbitMQ + envelope) e `POST /cart/checkout` publicando `cart.checked_out` | Cart | T-008 | todo |
| T-011 | Orders consome `cart.checked_out`, persiste no SQL Server, publica `order.placed`; `GET /orders` do comprador | Orders | T-009, T-010 | todo |
| T-012 | Catalog baixa estoque em `order.placed` (idempotente) → `stock.reserved`/`stock.rejected`; Orders atualiza status; Cart limpa o carrinho | Catalog/Orders/Cart | T-011 | todo |

## Fase 5 — Front
| ID | Tarefa | Serviço | Depende | Status |
|---|---|---|---|---|
| T-013 | Setup Vitest + Testing Library, cliente HTTP tipado, roteamento; telas de cadastro/login | Web | T-004 | todo |
| T-014 | Lista e detalhe de produtos, "adicionar ao carrinho"; tela de cadastro de produto (Seller) | Web | T-006, T-013 | todo |
| T-015 | Tela de carrinho, checkout e "meus pedidos" | Web | T-012, T-014 | todo |
| T-016 | Teste E2E do caminho feliz (Playwright) rodando no CI com `docker compose` | Infra | T-015 | todo |

## Ideias (pós-MVP)
| ID | Ideia | Status |
|---|---|---|
| I-01 | Outbox pattern para publicar eventos de forma transacional | idea |
| I-02 | Pagamento em modo teste (Stripe/Mercado Pago) com split para o vendedor | idea |
| I-03 | Painel do vendedor (vendas, estoque) | idea |
| I-04 | Avaliações de comprador ↔ vendedor | idea |
| I-05 | Observabilidade: OpenTelemetry + trace distribuído entre .NET e Go | idea |
| I-06 | CD: publicar imagens no GHCR e deploy automático | idea |
| I-08 | Rate limit nas tentativas de login (no gateway ou no Users) + refresh token | idea |
| I-07 | Rotas do gateway: evitar `/api/users/users` (não remover o prefixo, ou serviços expõem a raiz do recurso). Decidir antes do T-006 | idea |
