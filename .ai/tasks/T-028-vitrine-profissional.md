# T-028 — Vitrine profissional e anúncio completo

- **Serviço:** Catalog + Web
- **Depende de:** T-027
- **Status:** done

## Objetivo
Deixar a loja com cara de marketplace de verdade: cabeçalho com busca e categorias, vitrine com filtros e
ordenação, página de produto completa e um formulário de anúncio com os dados que todo marketplace pede.

## Critérios de aceite (cada um vira ao menos um teste)
- [x] O produto aceita categoria (lista fechada, padrão "outros"), marca, SKU, condição (novo/usado/recondicionado),
      até 8 fotos (URL http/https), preço "de" (maior que o preço), peso, dimensões da embalagem, garantia,
      frete grátis e ficha técnica (até 20 pares nome/valor). Tudo opcional; inválido → 400 no campo.
- [x] `GET /products` aceita `q` (nome e marca), `category`, `condition`, `minPrice`, `maxPrice`,
      `freeShipping`, `sort` (`newest` padrão, `price_asc`, `price_desc`, `name`); filtro inválido → 400.
- [x] Cabeçalho com busca, barra de categorias, ícone do carrinho com contador e rodapé.
- [x] Vitrine: banner e categorias na página inicial; filtros (categoria, condição, faixa de preço, frete grátis),
      ordenação, contagem de resultados e paginação que preserva os filtros (tudo na URL, em português).
- [x] Cartão: foto, marca, % de desconto, preço "de", selo de frete grátis e de condição.
- [x] Página do produto: trilha com categoria, galeria, desconto, frete/garantia/estoque, ficha técnica,
      descrição e "Você também pode gostar" (mesma categoria).
- [x] Anúncio em 5 seções (principal, fotos, descrição + ficha técnica, preço, envio e garantia) com
      pré-visualização do cartão; valida no navegador o que dá (preço "de", URL das fotos, números inteiros).

## Testes esperados
- Unit (Catalog): `ProductTests`, `CreateProductHandlerTests`, `QueryProductsHandlerTests`.
- Integração (Catalog): detalhes salvos e devolvidos; filtros + busca + ordenação no SQL Server; 400 em filtro inválido.
- Web: `HomePage`, `ProductPage`, `NewProductPage` (Vitest + MSW).

## Fora de escopo
- Upload de imagem (segue como I-13: MinIO + presigned URL). Aqui a foto é só um endereço http(s).
- Editar anúncio, avaliações, parcelamento, cálculo de frete, nome da loja do vendedor (o Users não expõe perfil público).

## Notas / decisões tomadas durante a tarefa
- Pedido do humano em 2026-10-20 ("deixar a tela profissional, mais informações no cadastro do produto, tudo que
  normalmente tem num marketplace, numa nova branch"). Feita fora da ordem do backlog, a pedido.
- Branch criada a partir de `feat/T-027-outbox` @ 4d270d9 (sem o trabalho não commitado da T-027), porque `main`
  ainda não tem a Fase 7. A T-027 também gera migration no Catalog: quem fizer merge por último regenera a migration
  dele sobre o snapshot do outro.
- Fotos e ficha técnica ficam em JSON na própria tabela `Products` (listas pequenas, sempre lidas com o produto).
- Categorias: códigos estáveis no Catalog (`ProductCategories`), nomes em português só no front (`catalog/categories.ts`).
- O rodapé usa `div` em vez de `ul/li` para não somar `listitem` nas telas de pedidos/envios (testes e E2E usam esse papel).
