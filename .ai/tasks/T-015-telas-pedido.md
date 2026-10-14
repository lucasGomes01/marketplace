# T-015 — Telas de finalizar pedido e "Meus pedidos"

- **Serviço:** Web
- **Depende de:** T-012, T-014b
- **Status:** done

## Objetivo
Fechar o fluxo no navegador: do carrinho a pessoa finaliza o pedido e acompanha o status dele
(Processando → Confirmado | Recusado com o motivo), e vê o histórico em "Meus pedidos".

## Critérios de aceite (cada um vira ao menos um teste)
- [x] Carrinho: "Finalizar pedido" chama `POST /api/cart/checkout`, esvazia o contador do cabeçalho e leva para
      `/pedidos/{orderId}`.
- [x] Erros do checkout em português, sem perder o carrinho: carrinho vazio (400 `cart`), carrinho alterado (409),
      indisponível (503).
- [x] `/pedidos/:id`: o pedido nasce de forma assíncrona → enquanto a API responde 404 ou `Pending`, mostra
      "Processando" e consulta de novo; para quando vira `Confirmed`/`Rejected`. Recusado mostra o motivo.
      Se demorar demais, para de consultar e avisa que ainda está em processamento.
- [x] `/pedidos/:id` de outra pessoa ou inexistente (depois do prazo) → "Pedido não encontrado".
- [x] `/pedidos`: lista do comprador (mais recentes primeiro) com data, quantidade de itens, total e status;
      vazio → convite para ver produtos. Link "Pedidos" no cabeçalho.
- [x] Rotas de pedido exigem login.

## Testes esperados
- Front (Vitest + Testing Library + MSW): API falsa de pedidos que cria o pedido no checkout e muda o status depois
  de algumas consultas.
- Manual no compose: comprar → ver Confirmado; última unidade → ver Recusado com motivo.

## Fora de escopo
- Pagamento, cancelamento, notificações em tempo real (polling basta no MVP).

## Notas / decisões tomadas durante a tarefa
- O pedido nasce assíncrono: `/pedidos/:id` aberto pelo checkout (`state.placed`) trata 404 como "ainda processando" e
  consulta de novo (1,5 s, até 40 vezes); aberto direto, 404 é "não encontrado" na hora.
- `orderPolling` é mutável só para os testes acelerarem (5 ms). API falsa de pedidos (`test/orders.ts`) imita o
  atraso: N consultas com 404, N com `Pending`, depois o resultado combinado.
- Testes de tela inteira ficavam instáveis com a máquina carregada → `asyncUtilTimeout` 3 s e `testTimeout` 15 s.
- Cabeçalho no celular: links mais compactos para caber "Pedidos" sem esconder o nome.
- Verificado no navegador com o compose: compra confirmada e compra recusada (última unidade) com o motivo.
