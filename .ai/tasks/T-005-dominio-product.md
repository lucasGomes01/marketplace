# T-005 — Domínio Product + Money

- **Serviço:** Catalog (`Catalog.Domain`, `Catalog.UnitTests`)
- **Depende de:** T-001
- **Status:** done

## Objetivo
Modelar o produto à venda e o dinheiro com as invariantes protegidas no domínio, sem banco nem HTTP.
Nicho ainda genérico (ver `context/product.md`): só os campos que todo marketplace tem.

## Critérios de aceite
### Money (value object)
- [x] `Money.Create(amount, currency = "BRL")`: valor ≥ 0, no máximo 2 casas decimais, moeda com 3 letras
      (normalizada para maiúsculas). Fora disso → `DomainException` com `Field = "price"`.
- [x] Igualdade por valor (`10.5 BRL == 10.50 BRL`).

### Product
- [x] `Product.Create(sellerId, name, description, price, stock, clock)` gera `Id` (Guid v7), `CreatedAt` e `UpdatedAt`.
- [x] `sellerId` vazio → `DomainException` (`Field = "sellerId"`).
- [x] Nome obrigatório (trim), até 120 caracteres → `Field = "name"`.
- [x] Descrição opcional (trim; vazia vira `null`), até 2000 caracteres → `Field = "description"`.
- [x] Preço maior que zero → `Field = "price"`.
- [x] Estoque ≥ 0 → `Field = "stock"`.
- [x] `ChangePrice(price, clock)` aplica a mesma regra de preço e atualiza `UpdatedAt`.
- [x] `AddStock(quantity, clock)`: quantidade > 0.
- [x] `RemoveStock(quantity, clock)`: quantidade > 0 e ≤ estoque atual; senão → `DomainException` (`Field = "stock"`).
      Vai ser usado na baixa de estoque do pedido (T-012).
- [x] `IsOwnedBy(userId)` diz se o produto pertence ao vendedor (para autorização no T-006).

## Testes esperados
- Unit: `MoneyTests` e `ProductTests`, um teste por critério (`[Theory]` onde houver variações).

## Fora de escopo
- Persistência e endpoints (T-006), imagens, categorias, busca.
- Operações aritméticas de `Money` (soma e multiplicação entram quando o Cart precisar, no T-007).

## Notas
- `DomainException(message, field)` segue o padrão do Users (cópia local: cada serviço é dono do seu domínio).
- 2026-10-13 (concluída): 34 testes unitários (`MoneyTests`, `ProductTests`).
- Quantidade ≤ 0 em `AddStock`/`RemoveStock` usa `Field = "quantity"`; remover mais que o estoque usa `"stock"`. Uma operação inválida não altera o estoque.
- `Money` é record: `10.5` e `10.50` são iguais; moeda validada só pelo formato (3 letras), sem lista ISO.
