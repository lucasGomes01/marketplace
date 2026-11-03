# ADR-006 — Mastercard para repasses e câmbio, com adaptador simulado

- **Data:** 2026-11-03
- **Status:** aceita (T-029, T-030)

## Contexto
O dono quis integrar a Mastercard **sem intermediador de pagamento** (Stripe, Mercado Pago). Cobrar um cartão de
verdade exige um adquirente, então o que é possível sem intermediador são as APIs da própria Mastercard no
developer.mastercard.com, que têm **sandbox** gratuito (produção exige contrato e verificação da empresa):

- **Mastercard Send — Disbursements**: empresa pagando uma pessoa num cartão. Encaixa no **repasse ao vendedor**.
- **Currency Conversion Calculator**: taxa de câmbio diária da Mastercard. Encaixa na vitrine.

Nesta sessão não havia credenciais nem acesso ao portal.

## Decisão
1. **Serviço novo `Payments` (.NET, camadas do ADR-004)**, dono dos repasses e do câmbio. Banco próprio (`PaymentsDb`),
   consome `cart.checked-out`, `stock.rejected` e `shipment.status-changed` no grupo `payments-service`. Não publica
   eventos ainda (sem outbox; ideia I-16).
2. **Portas** `IPayoutGateway` e `IExchangeRateProvider` com dois adaptadores cada, escolhidos por
   `Payments:Provider`:
   - `Simulated` (**padrão**: dev, CI, E2E): aprova qualquer Mastercard menos `5105 1051 0510 5100`; câmbio por tabela.
   - `Mastercard`: sandbox real. Precisa de `Mastercard:ConsumerKey`, `SigningKeyPath` (.p12 ou .pem, em `./secrets`,
     fora do git), `SigningKeyPassword` e `PartnerId`.
3. **OAuth 1.0a implementado no próprio serviço** (`MastercardOAuthSigner`, ~100 linhas: `oauth_body_hash`, base string,
   RSA-SHA256), em vez do SDK `Mastercard.Developer.OAuth1Signer`: evita dependência nova e fica testado
   (assinatura verificada com a chave pública).
4. **Só Mastercard** para receber: o Send paga em cartões da bandeira. Validação de BIN (51–55, 2221–2720) + Luhn no
   domínio e no front.
5. **Número do cartão cifrado** com AES-256-GCM (`Payments:CardEncryptionKey`, 32 bytes base64, fora do banco).
   Respostas e logs só têm os 4 últimos dígitos; `PayoutTransfer.ToString()` não mostra o número.
6. **Pagar só depois do "Recebi"** (+ carência configurável): o vendedor recebe quando a venda está garantida.
7. **Idempotência do pagamento**: referência fixa por repasse (`MKT<id>`); 409 da Mastercard → consulta pela referência.
   Gravação logo depois de cada envio; `rowversion` contra dois despachantes no mesmo repasse.

## Consequências
- O fluxo inteiro roda sem credenciais (simulado) e troca para o sandbox só com configuração.
- O contrato exato do Disbursements foi montado pela documentação pública: **conferir os campos** ao ligar com
  credenciais (I-18). O sandbox da Mastercard responde de forma simulada por prefixo de referência.
- Guardar número de cartão traz obrigações de PCI DSS em produção (cofre/tokenização, rotação de chave). Aqui é
  aceitável por ser sandbox; em produção, trocar por token do MDES ou de um cofre.
- Uma só instância do despachante por vez (como o relay do outbox antes do lock): mais réplicas → I-17.
- A cobrança do comprador continua fora (I-02).
