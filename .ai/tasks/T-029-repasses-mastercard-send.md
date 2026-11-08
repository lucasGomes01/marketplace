# T-029 — Repasse ao vendedor pelo Mastercard Send

- **Serviço:** Payments (novo, .NET) · Gateway · Web · Infra
- **Depende de:** T-027
- **Status:** done (pedida pelo dono em 2026-11-03: "Payouts + conversão", serviço em .NET; sem credenciais da
  Mastercard nesta sessão → adaptador simulado por padrão, Mastercard ligado por configuração)

## Objetivo
O dinheiro de cada venda chega ao vendedor: no checkout o marketplace registra quanto cada vendedor tem a receber
(bruto − taxa da plataforma), e quando o comprador confirma a entrega ("Recebi") o líquido é pago **num cartão
Mastercard do vendedor** pelo **Mastercard Send (Disbursements)**. Primeiro consumidor de `shipment.status-changed`.

## Critérios de aceite
- [x] `cart.checked-out` cria **um repasse por vendedor** do pedido: bruto = soma dos itens dele, taxa = 10%
      (configurável, arredondada no centavo, meio centavo para cima), líquido = bruto − taxa. Repetido não duplica.
- [x] `stock.rejected` cancela os repasses do pedido que ainda aguardam entrega (`Canceled`, com o motivo).
- [x] `shipment.status-changed` com `Delivered` libera o repasse **daquele vendedor**: `Scheduled` (após a carência
      `Payouts:HoldDays`, padrão 0) ou `AwaitingCard` se ele ainda não cadastrou cartão. Outras etapas são ignoradas.
      Entrega antes da venda conhecida → nova tentativa (o consumidor reprocessa).
- [x] `PUT /payouts/card` (só Seller): aceita só **Mastercard** (BIN 51–55 e 2221–2720, Luhn, 16 dígitos), validade não
      vencida e nome. Guarda o número **cifrado (AES-256-GCM)** e devolve só os 4 últimos. Cadastrar de novo troca.
      Repasses `AwaitingCard`/`Failed` voltam a ser agendados.
- [x] Um despachante em segundo plano (a cada 5 s) envia os repasses vencidos: aprovado → `Sent` (guarda a referência
      do provedor); recusado → `Failed` (com o motivo; tenta de novo quando o cartão muda); falha temporária → continua
      `Scheduled` para daqui a `RetryDelaySeconds`. Cartão vencido → recusado sem chamar a Mastercard.
- [x] Referência do pagamento estável por repasse (`MKT<id>`): reenviar não paga duas vezes (409 da Mastercard →
      consulta pela referência).
- [x] `GET /payouts` (só Seller): cartão (mascarado), totais por moeda (a receber / recebido) e repasses.
- [x] Tela **Repasses** (`/vender/repasses`, link no cabeçalho para vendedores): cadastro/troca do cartão com validação
      no navegador (mesmas regras), lista com venda, taxa, "você recebe" e o próximo passo de cada repasse.
- [x] E2E: depois do "Recebi", a vendedora vê o repasse "Pago no seu cartão" com R$ 359,82 (399,80 − 10%).

## Testes
- Unit (Payments.UnitTests): `CardNumber`, `CardExpiry`, `PlatformFee`, máquina de status do `Payout`, `PayoutCard`;
  handlers com fakes (fluxo completo, idempotência, carência, sem cartão, recusa, queda); AES-GCM; assinatura OAuth 1.0a
  (verificada com a chave pública); adaptador Mastercard Send com `HttpMessageHandler` falso; simulador; arquitetura.
- Integração (Payments.IntegrationTests): SQL Server + Kafka reais; eventos publicados no Kafka → repasse pago,
  sem cartão → pago depois do cadastro, cartão recusado, pedido recusado; cartão cifrado no banco; 401/403.
- Gateway: rotas `/api/payouts`, `/api/exchange-rates`, `/health/payments`. Front: `PayoutsPage.test.tsx`, `card.test.ts`.

## Fora de escopo (viraram ideias)
- Publicar `payout.sent`/`payout.failed` (I-16). Várias réplicas do despachante com lock (I-17).
- Cobrança do comprador (I-02 continua aberta): este fluxo só paga o vendedor; o dinheiro da compra é simulado.
- Testar contra o sandbox real: precisa de conta no developer.mastercard.com (I-18).

## Notas / decisões
- ADR-006: Mastercard como provedor, simulado por padrão, OAuth 1.0a implementado sem SDK, cartão cifrado.
- Cartão de teste recusado no simulador: `5105 1051 0510 5100` (imita o cenário de recebedor inelegível do sandbox).
- O contrato do Disbursements (`POST /send/v1/partners/{partnerId}/disbursements/payment`, campos em snake_case, valor
  em centavos, `recipient_account_uri = pan:...;exp=AAAA-MM`) foi montado a partir da documentação pública; o portal
  não estava acessível desta sessão. **Conferir os campos ao ligar com credenciais.**
