# T-030 — Preço em outra moeda com a cotação da Mastercard

- **Serviço:** Payments · Gateway · Web
- **Depende de:** T-029 (mesmo serviço e mesma configuração da Mastercard)
- **Status:** done (pedida junto com a T-029 em 2026-11-03)

## Objetivo
Na página do produto, quem compra de fora (ou só quer comparar) vê o preço aproximado em USD, EUR, GBP ou ARS, com a
taxa de câmbio publicada pela Mastercard (**Currency Conversion Calculator**). Informativo: a compra continua na moeda
do produto.

## Critérios de aceite
- [x] `GET /exchange-rates/convert?amount=&from=&to=` (público): valor convertido (centavos, meio para cima), taxa,
      data da taxa e origem (`mastercard`, `simulated` ou `identity` quando as moedas são iguais).
- [x] Validação por campo (400): valor > 0, moedas ISO 4217; moeda que o provedor não cota → `currency.unsupported`.
- [x] Provedor fora do ar → 503 `exchange_rate.unavailable`.
- [x] Cada par de moedas fica em cache por 60 min (`Payments:ExchangeRateCacheMinutes`); a Mastercard publica uma taxa
      por dia.
- [x] Página do produto: "Ver em outra moeda" (select). Só consulta ao escolher; mostra "≈ US$ 16,41 · Cotação
      Mastercard de 03/11/2026. A compra é em BRL." ou o aviso de indisponível.

## Testes
- Unit: handler (arredondamento, mesma moeda sem chamada, validação, indisponível), adaptador Mastercard
  (URL com `fxDate=0000-00-00`, leitura de `data.conversionRate`, `errorCode` → moeda não suportada), simulador, cache.
- Integração: conversão sem login e 400 por campo. Front: `PriceConverter.test.tsx`; E2E escolhe USD na página do produto.

## Notas
- Endpoint: `GET /settlement/currencyrate/conversion-rate?fxDate=0000-00-00&transCurr=BRL&crdhldBillCurr=USD&bankFee=0&transAmt=1`
  no sandbox `https://sandbox.api.mastercard.com`, assinado com OAuth 1.0a como o Send.
- Simulado: tabela fixa aproximada (USD, BRL, EUR, GBP, ARS, CLP, MXN, JPY, CAD).
