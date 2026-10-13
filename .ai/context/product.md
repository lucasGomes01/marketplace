# Produto

## Visão
Marketplace multi-vendedor: vendedores cadastram produtos, compradores montam um carrinho com itens
de vários vendedores e fecham um pedido. A plataforma intermedia a venda (e, no futuro, fica com uma comissão).

> **Nicho: A DEFINIR.** Enquanto não for escolhido, o domínio é genérico ("produto").
> Quando for definido, atualize este arquivo e registre a decisão em `decisions/`.

## Personas
> **Uma conta só** (T-019): toda conta compra; vender é um perfil que a pessoa ativa em "Minha conta" →
> "Começar a vender". Os papéis abaixo são capacidades da mesma conta, não tipos de conta diferentes.

- **Comprador (Buyer):** toda conta. Navega, adiciona ao carrinho, finaliza o pedido, acompanha os pedidos.
- **Vendedor (Seller):** contas com o perfil de vendedor ativo. Cadastra e gerencia produtos e estoque, vê os
  pedidos que contêm seus itens. Continua podendo comprar.
- **Admin:** (pós-MVP) modera vendedores e produtos.

## Fluxo do MVP (objetivo inicial)
**usuário → produto → carrinho → pedido**
1. A pessoa se cadastra e faz login (JWT). Para vender, ativa o perfil de vendedor na própria conta.
2. O vendedor cadastra um produto (nome, descrição, preço, estoque).
3. O comprador lista e vê produtos e adiciona ao carrinho.
4. O comprador faz checkout → pedido criado com snapshot dos preços → estoque baixado → carrinho limpo.
5. O comprador vê "meus pedidos" com o status de cada um.

## Fora do MVP (ideias futuras)
Pagamento real e split de comissão · avaliações · busca avançada · painel do vendedor ·
disputas · notificações · imagens de produto.

## Glossário
| Termo | Significado |
|---|---|
| Seller / Vendedor | Conta com o perfil de vendedor ativo (papel `Seller` no token); dona de produtos |
| Buyer / Comprador | Toda conta (papel `Buyer` sempre presente); dona de carrinho e pedidos |
| Product | Item à venda de um Seller. Preço em `Money` (decimal + moeda BRL) |
| Cart | Carrinho de um Buyer. Um carrinho ativo por Buyer |
| Checkout | Ação de transformar o carrinho em pedido |
| Order | Pedido; guarda **snapshot** de nome e preço dos itens no momento do checkout |
| OrderStatus | `Pending` → `Confirmed` \| `Rejected` (pós-MVP: `Paid`, `Shipped`, `Delivered`, `Cancelled`) |
