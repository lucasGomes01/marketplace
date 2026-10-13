# T-014b — Telas do carrinho

- **Serviço:** Web
- **Depende de:** T-008, T-014
- **Status:** done

## Objetivo
Comprar de verdade até o carrinho: adicionar pela página do produto, ver e ajustar o carrinho.

## Critérios de aceite
### Página do produto
- [x] Botão **Adicionar ao carrinho** (substitui "Carrinho em breve").
- [x] Sem login → vai para "Entrar" e volta para o produto depois.
- [x] Esgotado → botão desabilitado "Esgotado". Produto da própria pessoa → desabilitado "Este produto é seu".
- [x] Sucesso → "Adicionado ao carrinho" com link "Ver carrinho"; o contador do cabeçalho atualiza.
- [x] Erros da API em português (estoque insuficiente: "Só há N em estoque."; Catalog fora: aviso de indisponível).
### Carrinho (`/carrinho`, exige login)
- [x] Vazio → "Seu carrinho está vazio." + link para a vitrine.
- [x] Lista itens com nome (link para o produto), preço unitário, quantidade, subtotal; total geral e quantidade de itens.
- [x] Botões − / + alteram a quantidade (− desabilitado em 1); **Remover** tira o item.
- [x] Erro ao alterar (ex.: sem estoque) aparece sem perder o carrinho.
- [x] "Finalizar pedido" visível, desabilitado com "em breve" (checkout é a T-015).
### Cabeçalho
- [x] Link **Carrinho** com a quantidade de itens para quem está logado; zera ao sair.

## Fora de escopo
- Checkout (T-015), carrinho de visitante sem login, escolher quantidade antes de adicionar.

## Notas
- 2026-10-13 (concluída). Front: 61 testes; lint e build limpos.
- `CartProvider` (contexto) guarda o carrinho da sessão atual; o contador do cabeçalho e as telas usam o mesmo estado. O carrinho "some" ao sair sem `setState` dentro de efeito (resultado guarda de qual token ele é).
- API falsa do carrinho nos testes (`src/test/cart.ts`) com as mesmas regras da real (estoque, próprio produto). Ids dos usuários falsos centralizados em `src/test/tokens.ts`.
- Mensagens do carrinho em português (contexto `cart` em `messages.ts`) + `cartErrorMessage` para 503/404/409.
- Verificado no navegador contra a API real: contador do cabeçalho, carrinho com totais, + até o limite do estoque ("Só há 5 em estoque."), "Este produto é seu", adicionar pela página do produto; celular sem nada para fora da tela.
- Painel do navegador oculto impede screenshots; a verificação foi feita por texto da página, cliques e medidas via JS.
