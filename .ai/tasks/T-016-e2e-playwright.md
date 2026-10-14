# T-016 — Teste E2E do caminho feliz (Playwright) no CI

- **Serviço:** Infra
- **Depende de:** T-015
- **Status:** done

## Objetivo
Um teste que usa o navegador de verdade contra o sistema inteiro (compose) e prova o fluxo do MVP a cada push.

## Critérios de aceite
- [x] Pasta `e2e/` (pacote próprio, Playwright + TypeScript), separada do front.
- [x] Caminho feliz pela interface: vendedora cria conta → "Começar a vender" → anuncia (estoque 3) → sai;
      comprador cria conta → adiciona 2 ao carrinho → finaliza → vê **Confirmado** → aparece em "Meus pedidos"
      → o produto passa a mostrar "Última unidade".
- [x] Roda no CI (job `e2e`): `docker compose --profile apps up -d --build`, espera os `/health/*` pelo gateway,
      front compilado servido pelo `vite preview` (repassa `/api` ao gateway).
- [x] Falhou → guarda relatório, trace, screenshots e logs do compose como artefato.

## Notas / decisões tomadas durante a tarefa
- O E2E achou **dois bugs reais** de corrida no front (intermitentes, ~1 em 5 execuções), cada um com teste de regressão:
  1. A carga inicial do carrinho (vazio) chegava depois do "Adicionar ao carrinho" e apagava o item do contador →
     a carga inicial não sobrescreve mais um carrinho já atualizado por uma operação (`CartProvider`).
  2. Clicar em "Adicionar" enquanto a sessão ainda carregava mandava para o login e o item se perdia →
     o botão fica desabilitado até a sessão carregar (`AddToCartButton`).
- Depois das correções: 10 execuções seguidas verdes.
- Contas e produtos com sufixo único por execução: o teste roda em banco que já tem dados.
- `retries: 1` só no CI; localmente `reuseExistingServer` reaproveita um `vite preview` já aberto.
