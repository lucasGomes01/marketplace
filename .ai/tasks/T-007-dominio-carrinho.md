# T-007 — Domínio do carrinho

- **Serviço:** Cart (`Cart.Domain`, `Cart.UnitTests`)
- **Depende de:** T-005
- **Status:** done

## Objetivo
Modelar o carrinho do comprador com as regras protegidas no domínio, sem banco nem HTTP.

## Regras
- Um carrinho por comprador. Itens de **vários vendedores** no mesmo carrinho.
- Cada item guarda um **snapshot** do produto (vendedor, nome, preço unitário) do momento em que foi adicionado;
  adicionar de novo o mesmo produto **soma a quantidade e atualiza o snapshot** para o preço/nome mais recente.
- Quantidade por item: de 1 a 99.
- Uma moeda por carrinho (o primeiro item define); item em outra moeda é recusado.
- O total é a soma de `preço × quantidade`.
- Estoque e "não comprar o próprio produto" são verificados na Application (precisam do Catalog).

## Critérios de aceite
- [x] `Money` local com `Add` e `Multiply` (mesmas regras de valor/moeda do Catalog).
- [x] `Cart.Create(buyerId, clock)` vazio; `buyerId` vazio → `DomainException`.
- [x] `AddItem(productId, sellerId, name, unitPrice, quantity, clock)`: novo item ou soma; total após somar > 99 → erro.
- [x] `ChangeQuantity(productId, quantity, clock)`: 1..99; item inexistente → erro (`Field = "productId"`).
- [x] `RemoveItem(productId, clock)`: item inexistente → erro.
- [x] `Clear(clock)` esvazia.
- [x] `Total`, `TotalQuantity`, `Currency`; carrinho vazio → total 0 BRL.
- [x] `QuantityOf(productId)` (0 se não está no carrinho), para a Application checar estoque.
- [x] Toda alteração atualiza `UpdatedAt`; operação inválida não altera nada.

## Fora de escopo
- Persistência, API, consulta ao Catalog (T-008); checkout (T-010).

## Notas
- 2026-10-13 (concluída): 23 testes (`ShoppingCartTests`, `MoneyTests`).
- A entidade se chama **`ShoppingCart`** (não `Cart`): o namespace raiz do serviço é `Cart` e o nome igual geraria ambiguidade no C#.
- `Money` do Cart é cópia local com `Add`/`Multiply`/`Zero` (o Catalog não precisa delas).
