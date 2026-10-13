# T-019 — Conta única: compra e venda na mesma conta

- **Serviço:** Users + BuildingBlocks.Auth + Web
- **Depende de:** T-018
- **Status:** done

## Objetivo
Hoje cada conta tem um papel fixo (Comprador **ou** Vendedor) escolhido no cadastro. Isso não é o que se espera de
um marketplace: a mesma conta deve comprar e, se quiser, vender. Decidido com o dono do projeto em 2026-10-13.

## Regras
- Toda conta pode **comprar** (papel `Buyer` sempre presente).
- O perfil de **vendedor** é ativado pela própria pessoa ("Começar a vender") e fica registrado com a data
  (`SellerSince`). Ativar de novo não muda nada (idempotente). Não há "deixar de vender" no MVP.
- O token traz **todos** os papéis ativos (`role: ["Buyer","Seller"]`); as policies `Buyer`/`Seller` continuam valendo.
- Contas que hoje são `Seller` viram "comprador + vendedor"; contas `Buyer` continuam só compradoras.

## Critérios de aceite
### Domínio / Application
- [x] `User.Register(email, name, passwordHash, clock)` (sem papel); `Roles` = `[Buyer]` ou `[Buyer, Seller]`.
- [x] `User.BecomeSeller(clock)`: ativa e grava `SellerSince`; repetir não altera a data.
- [x] `BecomeSellerHandler`: ativa o perfil e devolve o usuário atualizado **e um token novo** (o antigo não tem o
      papel `Seller`). Usuário inexistente → `NotFound`.
- [x] `UserResponse` com `roles` (lista) em vez de `role`.
### API
- [x] `POST /users` não recebe mais papel (campo extra é ignorado).
- [x] `POST /users/me/seller` (autenticado) → `200 { user, accessToken, tokenType, expiresAt }`.
- [x] Migration converte os dados: `Role = 'Seller'` → `IsSeller = 1`, `SellerSince = CreatedAt`; coluna `Role` removida.
### BuildingBlocks.Auth
- [x] `JwtTokenIssuer.Issue(userId, email, roles)` emite um claim `role` por papel; `IsInRole` funciona para cada um.
### Web
- [x] Cadastro sem escolha de tipo de conta.
- [x] Minha conta mostra "Comprador" ou "Comprador e vendedor"; quem não vende vê o botão **Começar a vender**,
      que ativa o perfil, troca a sessão pelo token novo e atualiza a tela sem precisar sair e entrar.

## Fora de escopo
- Desativar o perfil de vendedor, dados de loja (nome fantasia, documento), aprovação de vendedores.

## Notas
- 2026-10-13 (concluída). .NET: Users.UnitTests 44, Users.IntegrationTests 22 (inclui `BecomeSellerTests` e `MigrationTests`), Auth 8. Front: 24.
- **Migration com conversão de dados:** o EF gerou o `DropColumn(Role)` antes de criar as colunas novas (perderia os vendedores). Reescrita na ordem segura: cria colunas → `UPDATE` → remove `Role`; `Down` reverte. Coberta por `MigrationTests` (banco novo no formato antigo → migra → confere) e aplicada no banco local: a conta da Ana virou comprador + vendedor.
- `POST /users` ignora um `role` enviado por clientes antigos (System.Text.Json ignora campos extras).
- Verificado no navegador: cadastro sem tipo de conta → "Comprador" + convite → "Começar a vender" → "Comprador e vendedor" na hora e após recarregar.
