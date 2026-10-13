# T-006 — API do catálogo de produtos

- **Serviço:** Catalog (`Catalog.Application`, `Catalog.Infrastructure`, `Catalog.Api`) — camadas do ADR-004
- **Depende de:** T-004, T-005, T-019
- **Status:** done

## Objetivo
Vendedores cadastram produtos; qualquer pessoa (mesmo sem login) navega pelo catálogo.

## Rotas (via gateway: `/api/products...`)
| Método | Rota | Quem | Resposta |
|---|---|---|---|
| `POST` | `/products` | policy `Seller` | `201` + `Location: /products/{id}` |
| `GET` | `/products?page=1&pageSize=20` | público | `200 { items, page, pageSize, totalCount }` |
| `GET` | `/products/{id}` | público | `200` ou `404` |

Produto na resposta: `{ id, sellerId, name, description, price, currency, stock, createdAt }`.

## Critérios de aceite
- [x] Criar produto como vendedor → `201`; o `sellerId` vem do token (nunca do corpo).
- [x] Sem token → `401`; conta que só compra → `403`.
- [x] Dados inválidos → `400` com erro por campo: nome vazio/longo, preço ≤ 0 ou com mais de 2 casas, moeda inválida,
      estoque negativo, preço ausente. Moeda padrão `BRL`.
- [x] Detalhe por id → `200`; id inexistente → `404` com `code = product.not_found`.
- [x] Lista paginada, **mais recentes primeiro**; `page ≥ 1`, `1 ≤ pageSize ≤ 50` (padrão 20), senão `400`.
- [x] Persistência em `CatalogDb` (EF Core), preço como `decimal(18,2)` + moeda `char(3)`; índices em `CreatedAt` e `SellerId`.
- [x] Testes: handlers com fakes, arquitetura (regra de dependência) e integração com SQL Server real (Testcontainers).

## Fora de escopo
- Editar/remover produto, imagens, busca e filtros, nome do vendedor na listagem (o Catalog não conhece nomes).

## Notas
- 2026-10-13 (concluída). Catalog.UnitTests 54 (domínio + handlers com fakes + arquitetura), Catalog.IntegrationTests 15.
- `Money` mapeado com `ComplexProperty` do EF (colunas `Price decimal(18,2)` e `Currency nchar(3)` na tabela Products).
- Moeda inválida agora aponta para o campo `currency` (antes `price`).
- Testes de integração emitem tokens com o `JwtTokenIssuer` da lib compartilhada (sem subir o Users).
- **Bug achado no teste manual:** JSON malformado devolvia **500** em Development (BadHttpRequestException → tratador de exceção). Corrigido para todos os serviços com `UseMarketplaceExceptionHandler()` (BuildingBlocks.Api), que preserva o status 400. Teste de regressão no Catalog e no Users.
- Cuidado ao testar com curl no Git Bash: argumentos com acento não saem em UTF-8; use `--data-binary @arquivo.json`.
- Produtos de exemplo criados no banco local pela conta da Ana.
