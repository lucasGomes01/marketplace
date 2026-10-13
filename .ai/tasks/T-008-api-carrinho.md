# T-008 — API do carrinho

- **Serviço:** Cart (`Cart.Application`, `Cart.Infrastructure`, `Cart.Api`) — camadas do ADR-004
- **Depende de:** T-006, T-007
- **Status:** done

## Objetivo
Comprador logado monta o carrinho. Preço, nome, vendedor e estoque vêm **do Catalog na hora** (HTTP), nunca do
corpo da requisição — quem chama não consegue escolher o próprio preço.

## Rotas (via gateway: `/api/cart...`) — todas exigem login (policy `Buyer`, que toda conta tem)
| Método | Rota | Corpo | Resposta |
|---|---|---|---|
| `GET` | `/cart` | — | `200` carrinho (vazio se ainda não existe) |
| `POST` | `/cart/items` | `{ productId, quantity }` | `200` carrinho atualizado |
| `PUT` | `/cart/items/{productId}` | `{ quantity }` | `200` carrinho atualizado |
| `DELETE` | `/cart/items/{productId}` | — | `200` carrinho atualizado |

Carrinho: `{ items: [{ productId, sellerId, productName, unitPrice, currency, quantity, subtotal }], totalQuantity, total, currency }`.

## Critérios de aceite
- [x] Adicionar consulta o Catalog: produto inexistente → `404 product.not_found`.
- [x] Quantidade total do produto no carrinho > estoque → `400` no campo `quantity` (`cart.insufficient_stock`);
      produto esgotado também.
- [x] Vendedor não compra o próprio produto → `400` (`cart.own_product`).
- [x] Adicionar de novo soma e atualiza o preço para o atual do Catalog.
- [x] Alterar quantidade revalida o estoque no Catalog.
- [x] Remover/alterar item que não está no carrinho → `404 cart.item_not_found`.
- [x] Catalog fora do ar → `503` com mensagem clara (sem 500).
- [x] Persistência em `CartDb` (EF Core), um carrinho por comprador (índice único), itens como tabela filha.
- [x] Concorrência otimista (`rowversion`): duas alterações simultâneas não se sobrescrevem → `409 cart.concurrent_update`.
- [x] Testes: handlers com fakes, arquitetura, cliente HTTP do Catalog (handler HTTP falso) e integração com SQL
      Server real e um Catalog falso.

## Fora de escopo
- Checkout (T-010), reservar estoque ao adicionar, carrinho de visitante sem login.

## Notas
- 2026-10-13 (concluída). Cart.UnitTests 48 (domínio + handlers com fakes + cliente HTTP do Catalog + arquitetura), Cart.IntegrationTests 13 (SQL Server real + Catalog falso via `ConfigureTestServices`).
- Novo tipo de erro compartilhado `ErrorType.Unavailable` → **503** (dependência fora do ar).
- Cliente HTTP do Catalog: timeout de 5 s; 404 → produto inexistente; 5xx/sem conexão/timeout → `CatalogUnavailableException` → 503. URL em `Services:Catalog` (compose: `http://catalog:8080`; local: `http://localhost:5002`).
- **Bug encontrado (EF Core):** adicionar um 2º produto a um carrinho existente dava 409. Investigado com log do EF: item novo de coleção *owned* com chave preenchida pela aplicação é rastreado como `Modified` → `UPDATE` de 0 linhas → falso conflito de concorrência. Correção no `CartRepository`: ele lembra os itens carregados e marca como `Added` os que não existiam. O teste de concorrência real continua detectando conflitos.
- Concorrência otimista com `rowversion` na tabela `Carts`; como toda alteração do domínio muda `UpdatedAt`, a linha do carrinho sempre é atualizada (inclusive quando só os itens mudam).
- Pacotes do Cart.Infrastructure fixados em 10.0.12 (o 10.0.13 do EF depende de pacote ainda não publicado).
- Validado no compose via gateway com o Catalog real: adicionar, somar, estoque insuficiente (400), próprio produto (400), total correto.
