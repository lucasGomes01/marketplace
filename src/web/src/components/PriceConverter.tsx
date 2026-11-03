import { useEffect, useId, useState } from 'react'
import { conversionCurrencies, exchangeRatesApi, type Conversion } from '../api/payments'
import { formatPrice } from '../utils/money'

type Load = { status: 'idle' } | { status: 'loading' } | { status: 'error' } | { status: 'ready'; conversion: Conversion }

/** "2026-11-03" → "03/11/2026" (sem passar por Date, para o fuso não mudar o dia). */
const formatRateDate = (date: string) => date.split('-').reverse().join('/')

/**
 * "Ver em outra moeda": converte o preço com a cotação da Mastercard. Só consulta quando a pessoa escolhe a moeda.
 * Informativo: a compra continua na moeda do produto.
 */
export function PriceConverter({ amount, currency }: { amount: number; currency: string }) {
  const selectId = useId()
  const [target, setTarget] = useState('')
  const [result, setResult] = useState<{ key: string; load: Load } | null>(null)
  const key = `${amount}:${currency}:${target}`
  const load: Load = !target ? { status: 'idle' } : result?.key === key ? result.load : { status: 'loading' }

  useEffect(() => {
    if (!target) return
    const controller = new AbortController()
    const requestKey = `${amount}:${currency}:${target}`
    exchangeRatesApi
      .convert(amount, currency, target, controller.signal)
      .then((conversion) => setResult({ key: requestKey, load: { status: 'ready', conversion } }))
      .catch(() => {
        if (!controller.signal.aborted) setResult({ key: requestKey, load: { status: 'error' } })
      })
    return () => controller.abort()
  }, [amount, currency, target])

  return (
    <div className="converter">
      <label htmlFor={selectId} className="converter__label">
        Ver em outra moeda
      </label>
      <select id={selectId} className="field__input converter__select" value={target} onChange={(e) => setTarget(e.target.value)}>
        <option value="">Escolha…</option>
        {conversionCurrencies
          .filter((c) => c !== currency)
          .map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
      </select>
      <p className="converter__result" aria-live="polite">
        {load.status === 'loading' ? (
          <span className="muted">Consultando cotação…</span>
        ) : load.status === 'error' ? (
          <span className="field__error">Não foi possível obter a cotação agora.</span>
        ) : load.status === 'ready' ? (
          <>
            <strong>≈ {formatPrice(load.conversion.convertedAmount, load.conversion.to)}</strong>{' '}
            <span className="muted">
              {load.conversion.source === 'mastercard' ? 'Cotação Mastercard' : 'Cotação simulada'} de{' '}
              {formatRateDate(load.conversion.rateDate)}. A compra é em {currency}.
            </span>
          </>
        ) : null}
      </p>
    </div>
  )
}
