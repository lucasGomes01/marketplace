# T-025 — Telas do envio

- **Serviço:** Web
- **Depende de:** T-024
- **Status:** done

## Objetivo
Vendedor despacha pelo navegador; comprador acompanha a entrega e confirma o recebimento.

## Critérios de aceite
- [x] **Vendedor** — "Despachar" no cabeçalho (só para quem tem perfil de vendedor) → `/vender/envios`:
      envios "A despachar" e "Enviados", cada um com os itens dele, o status e o botão da próxima etapa
      ("Marcar como separado" → "embalado" → "enviado"). Conta sem perfil de vendedor vê o convite para vender.
- [x] Conflito (409) ao avançar → aviso e a lista é recarregada.
- [x] **Comprador** — no pedido confirmado, seção "Entrega" com um bloco por envio: itens, linha do tempo
      (Separado, Embalado, Enviado, Entregue com data) e o botão "Recebi" quando o envio está `Shipped`.
- [x] Em "Meus pedidos", pedidos confirmados mostram o andamento da entrega (ex.: "Enviado", "Parcialmente entregue").

## Testes esperados
- Vitest + Testing Library + MSW (API falsa ganha `/api/shipments` e a confirmação de recebimento).

## Fora de escopo
- Nome do vendedor no envio (o Orders só conhece o id), rastreio (I-15), cancelamento (I-14).

## Notas
- 89 testes de front verdes (`npm test`), lint e build limpos. Datas em `utils/dates.ts` (saíram da OrderPage).
