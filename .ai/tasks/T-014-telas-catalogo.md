# T-014 — Telas do catálogo

- **Serviço:** Web
- **Depende de:** T-006, T-013
- **Status:** done

## Objetivo
Ver o catálogo no navegador: vitrine, página do produto e cadastro de produto para quem vende.

## Telas
| Rota | Quem | O que mostra |
|---|---|---|
| `/` | todos | Vitrine: grade de produtos (mais recentes primeiro), 12 por página, paginação `?pagina=N` |
| `/produtos/:id` | todos | Nome, preço, descrição, estoque; botão de carrinho desabilitado ("Carrinho em breve" — T-014b) |
| `/vender/novo` | logado | Formulário de produto. Conta que só compra vê o convite "Começar a vender" antes do formulário |

## Critérios de aceite
- [x] Preço formatado em reais (`R$ 199,90`) via `Intl.NumberFormat('pt-BR')`, respeitando a moeda do produto.
- [x] Estoque: "Esgotado" quando 0, "Últimas N unidades" quando ≤ 3, senão "N em estoque".
- [x] Vitrine vazia: "Nenhum produto à venda ainda." (com convite para vender).
- [x] Paginação com "Anterior"/"Próxima" que só aparecem quando fazem sentido.
- [x] Produto inexistente: "Produto não encontrado" com link para a vitrine.
- [x] Cadastro aceita preço no formato brasileiro (`1.234,56`); preço não numérico é barrado no front.
- [x] Erros da API aparecem em português embaixo do campo.
- [x] Sucesso → página do produto com "Produto publicado!".
- [x] Cabeçalho com link "Vender" para quem está logado.
- [x] Testes de cada tela com MSW; `npm test`, `lint` e `build` verdes; verificação no navegador contra a API real.

## Fora de escopo
- Adicionar ao carrinho (T-014b), editar/remover produto, imagens, busca.

## Notas
- 2026-10-13 (concluída). Front: 49 testes (vitrine, produto, cadastro de produto, `money`). Lint e build limpos.
- Verificado no navegador contra a API real: vitrine com produtos reais; publicar "1.299,90" → R$ 1.299,90 com acentos preservados; página do produto com "Produto publicado!".
- **Ajustes vindos da verificação visual:** (1) na tela estreita a imagem do produto empurrava nome e preço para fora da tela → proporção menor no celular; (2) no celular (375px) o botão "Sair" ficava fora da tela (até 445px) → cabeçalho encolhe o nome com reticências e esconde a palavra "Marketplace" abaixo de 400px; vitrine em 2 colunas no celular. Medido de novo: "Sair" termina em 359px, sem rolagem horizontal.
- Lint (oxlint) apontou `setState` síncrono dentro de `useEffect` → o "carregando" agora é derivado (resultado guarda de qual página/produto ele é).
- Traduções de erro por contexto (`account` x `product`) em `src/api/messages.ts`.
- O convite "Começar a vender" virou o componente `BecomeSellerCallout` (Minha conta e Anunciar produto).
- Ainda sem imagens: o "thumb" é a inicial do produto.
