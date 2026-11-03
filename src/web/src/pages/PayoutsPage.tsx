import { useEffect, useState, type FormEvent } from 'react'
import { ApiError, type FieldErrors } from '../api/client'
import { translateFieldErrors } from '../api/messages'
import { payoutsApi, type Payout, type PayoutCard, type SellerPayouts } from '../api/payments'
import { isSeller } from '../api/users'
import { useAuth } from '../auth/context'
import { BecomeSellerCallout } from '../components/BecomeSellerCallout'
import { FormField } from '../components/FormField'
import { PayoutStatusBadge } from '../components/PayoutStatusBadge'
import { cardDigits, cardNumberError, formatCardNumber, parseExpiry } from '../utils/card'
import { formatDate, shortOrderId } from '../utils/dates'
import { formatPrice } from '../utils/money'

/** "Repasses": o vendedor cadastra o cartão Mastercard de recebimento e acompanha o que tem a receber. */
export function PayoutsPage() {
  const { state } = useAuth()

  if (state.status !== 'authenticated') {
    return null
  }
  if (!isSeller(state.user)) {
    return (
      <section className="panel">
        <h1>Repasses</h1>
        <BecomeSellerCallout
          title="Ative a venda na sua conta"
          description="O dinheiro das suas vendas é pago aqui, direto no seu cartão Mastercard."
        />
      </section>
    )
  }
  return <SellerPayoutsView token={state.token} />
}

function SellerPayoutsView({ token }: { token: string }) {
  const [reload, setReload] = useState(0)
  const [result, setResult] = useState<{ token: string; data: SellerPayouts | 'error' } | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    payoutsApi
      .list(token, controller.signal)
      .then((data) => setResult({ token, data }))
      .catch(() => {
        if (!controller.signal.aborted) setResult({ token, data: 'error' })
      })
    return () => controller.abort()
  }, [token, reload])

  const data = result?.token === token ? result.data : null
  if (data === null) {
    return (
      <p className="muted" role="status">
        Carregando repasses…
      </p>
    )
  }
  if (data === 'error') {
    return (
      <section className="payouts">
        <h1>Repasses</h1>
        <div className="alert alert--error" role="alert">
          Não foi possível carregar seus repasses. Tente novamente em instantes.
        </div>
      </section>
    )
  }

  function cardSaved() {
    setNotice('Cartão salvo. Os repasses liberados serão pagos nele.')
    setReload((n) => n + 1)
  }

  return (
    <section className="payouts">
      <h1>Repasses</h1>
      {notice ? (
        <div className="alert alert--success" role="status">
          {notice}
        </div>
      ) : null}

      <CardSection card={data.card} token={token} onSaved={cardSaved} />

      <section className="payouts__summary" aria-label="Totais">
        {data.totals.length === 0 ? null : (
          data.totals.map((t) => (
            <dl key={t.currency} className="details payouts__totals">
              <div>
                <dt>A receber</dt>
                <dd>{formatPrice(t.pending, t.currency)}</dd>
              </div>
              <div>
                <dt>Recebido</dt>
                <dd>{formatPrice(t.sent, t.currency)}</dd>
              </div>
            </dl>
          ))
        )}
      </section>

      <section className="payouts__group" aria-labelledby="payouts-list">
        <h2 id="payouts-list">Vendas</h2>
        {data.payouts.length === 0 ? (
          <div className="panel empty-state">
            <p>Nenhuma venda ainda. Quando alguém comprar seus produtos, o valor aparece aqui.</p>
          </div>
        ) : (
          <div className="shipments__list">
            {data.payouts.map((payout) => (
              <PayoutCardView key={payout.id} payout={payout} />
            ))}
          </div>
        )}
      </section>
    </section>
  )
}

function CardSection({ card, token, onSaved }: { card: PayoutCard | null; token: string; onSaved: () => void }) {
  const [editing, setEditing] = useState(false)

  return (
    <section className="panel payouts__card" aria-labelledby="payout-card">
      <h2 id="payout-card">Cartão de recebimento</h2>
      {card && !editing ? (
        <div className="payouts__card-current">
          <p>
            <strong>
              {card.brand} •••• {card.last4}
            </strong>{' '}
            <span className="muted">
              · validade {String(card.expiryMonth).padStart(2, '0')}/{String(card.expiryYear % 100).padStart(2, '0')} ·{' '}
              {card.holderName}
            </span>
          </p>
          <button type="button" className="button button--ghost" onClick={() => setEditing(true)}>
            Trocar cartão
          </button>
        </div>
      ) : (
        <>
          <p className="muted">
            Pagamos suas vendas pelo Mastercard Send, direto num cartão Mastercard seu, depois que o comprador confirma o
            recebimento. Guardamos o número cifrado e mostramos só os 4 últimos dígitos.
          </p>
          <CardForm
            token={token}
            onCancel={card ? () => setEditing(false) : undefined}
            onSaved={() => {
              setEditing(false)
              onSaved()
            }}
          />
        </>
      )}
    </section>
  )
}

function CardForm({ token, onSaved, onCancel }: { token: string; onSaved: () => void; onCancel?: () => void }) {
  const [number, setNumber] = useState('')
  const [expiry, setExpiry] = useState('')
  const [holderName, setHolderName] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const [failure, setFailure] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  function validate(): FieldErrors {
    const found: FieldErrors = {}
    const numberError = cardNumberError(number)
    if (numberError) found.cardNumber = numberError
    if (!parseExpiry(expiry)) found.expiry = 'Informe a validade como MM/AA.'
    if (!holderName.trim()) found.holderName = 'Informe o nome impresso no cartão.'
    return found
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFailure(null)
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length > 0) return

    const parsed = parseExpiry(expiry)!
    setSaving(true)
    try {
      await payoutsApi.registerCard(
        { cardNumber: cardDigits(number), expiryMonth: parsed.month, expiryYear: parsed.year, holderName: holderName.trim() },
        token,
      )
      onSaved()
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        setErrors(translateFieldErrors(error.fieldErrors, 'payoutCard'))
      } else {
        setFailure('Não foi possível salvar o cartão. Tente novamente.')
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="form" onSubmit={handleSubmit} noValidate>
      {failure ? (
        <div className="alert alert--error" role="alert">
          {failure}
        </div>
      ) : null}
      <FormField
        label="Número do cartão Mastercard"
        name="cardNumber"
        inputMode="numeric"
        autoComplete="cc-number"
        placeholder="5555 5555 5555 4444"
        value={number}
        onChange={(e) => setNumber(formatCardNumber(e.target.value))}
        error={errors.cardNumber}
      />
      <div className="form__row">
        <FormField
          label="Validade"
          name="expiry"
          inputMode="numeric"
          autoComplete="cc-exp"
          placeholder="MM/AA"
          value={expiry}
          onChange={(e) => setExpiry(e.target.value)}
          error={errors.expiry}
        />
        <FormField
          label="Nome impresso no cartão"
          name="holderName"
          autoComplete="cc-name"
          value={holderName}
          onChange={(e) => setHolderName(e.target.value)}
          error={errors.holderName}
        />
      </div>
      <div className="payouts__actions">
        <button type="submit" className="button button--primary" disabled={saving}>
          {saving ? 'Salvando…' : 'Salvar cartão'}
        </button>
        {onCancel ? (
          <button type="button" className="button button--ghost" onClick={onCancel}>
            Cancelar
          </button>
        ) : null}
      </div>
    </form>
  )
}

function payoutDetail(p: Payout): string {
  switch (p.status) {
    case 'AwaitingDelivery':
      return 'Liberado quando o comprador confirmar o recebimento.'
    case 'AwaitingCard':
      return 'Entregue! Cadastre um cartão Mastercard para receber.'
    case 'Scheduled':
      return p.scheduledFor ? `Pagamento previsto para ${formatDate(p.scheduledFor)}.` : 'Pagamento agendado.'
    case 'Sent':
      return p.sentAt ? `Pago no seu cartão em ${formatDate(p.sentAt)}.` : 'Pago no seu cartão.'
    case 'Failed':
      return `A Mastercard recusou o pagamento${p.failureReason ? ` (${p.failureReason})` : ''}. Troque o cartão para tentarmos de novo.`
    case 'Canceled':
      return `Pedido recusado${p.failureReason ? `: ${p.failureReason}` : ''}. Nada a receber.`
  }
}

function PayoutCardView({ payout }: { payout: Payout }) {
  const title = `Pedido ${shortOrderId(payout.orderId)}`
  return (
    <article className="shipment panel" aria-label={title}>
      <header className="shipment__header">
        <h3>{title}</h3>
        <PayoutStatusBadge status={payout.status} />
      </header>
      <dl className="payout__amounts">
        <div>
          <dt>Venda</dt>
          <dd>{formatPrice(payout.gross, payout.currency)}</dd>
        </div>
        <div>
          <dt>Taxa</dt>
          <dd>− {formatPrice(payout.fee, payout.currency)}</dd>
        </div>
        <div>
          <dt>Você recebe</dt>
          <dd>
            <strong>{formatPrice(payout.net, payout.currency)}</strong>
          </dd>
        </div>
      </dl>
      <p className="muted">{payoutDetail(payout)}</p>
    </article>
  )
}
