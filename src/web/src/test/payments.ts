import { http, HttpResponse } from 'msw'
import type { Payout, PayoutCard } from '../api/payments'
import { isSellerAuthorization } from './tokens'

/** Repasses e cartão da vendedora logada na API falsa; recriados a cada teste. */
let card: PayoutCard | null = null
let payouts: Payout[] = []

export function resetFakePayments() {
  card = null
  payouts = []
}

export function seedPayoutCard(seed: Partial<PayoutCard> = {}) {
  card = {
    brand: 'Mastercard',
    last4: '4444',
    expiryMonth: 12,
    expiryYear: 2029,
    holderName: 'Maria Silva',
    updatedAt: '2026-11-01T10:00:00Z',
    ...seed,
  }
}

export function seedPayout(seed: Partial<Payout> = {}): Payout {
  const payout: Payout = {
    id: crypto.randomUUID(),
    orderId: crypto.randomUUID(),
    status: 'AwaitingDelivery',
    gross: 100,
    fee: 10,
    net: 90,
    currency: 'BRL',
    createdAt: '2026-11-01T10:00:00Z',
    deliveredAt: null,
    scheduledFor: null,
    sentAt: null,
    providerReference: null,
    failureReason: null,
    ...seed,
  }
  payouts.unshift(payout)
  return payout
}

const pendingStatuses = ['AwaitingDelivery', 'AwaitingCard', 'Scheduled', 'Failed']

/** Taxas simuladas: quantos dólares vale 1 unidade. */
const usdPerUnit: Record<string, number> = { USD: 1, BRL: 0.1834, EUR: 1.087, GBP: 1.2804, ARS: 0.00105 }

const forbidden = () => HttpResponse.json({ status: 403 }, { status: 403 })

export const paymentHandlers = [
  http.get('/api/payouts', ({ request }) => {
    if (!isSellerAuthorization(request.headers.get('Authorization'))) return forbidden()
    const currencies = [...new Set(payouts.map((p) => p.currency))]
    const sum = (list: Payout[]) => Math.round(list.reduce((total, p) => total + p.net, 0) * 100) / 100
    return HttpResponse.json({
      card,
      totals: currencies.map((currency) => {
        const mine = payouts.filter((p) => p.currency === currency)
        return {
          currency,
          pending: sum(mine.filter((p) => pendingStatuses.includes(p.status))),
          sent: sum(mine.filter((p) => p.status === 'Sent')),
        }
      }),
      payouts,
    })
  }),

  http.put('/api/payouts/card', async ({ request }) => {
    if (!isSellerAuthorization(request.headers.get('Authorization'))) return forbidden()
    const body = (await request.json()) as { cardNumber: string; expiryMonth: number; expiryYear: number; holderName: string }
    if (body.cardNumber.startsWith('4')) {
      return HttpResponse.json(
        { status: 400, title: 'One or more validation errors occurred.', errors: { cardNumber: ['Only Mastercard cards are accepted.'] } },
        { status: 400 },
      )
    }
    card = {
      brand: 'Mastercard',
      last4: body.cardNumber.slice(-4),
      expiryMonth: body.expiryMonth,
      expiryYear: body.expiryYear,
      holderName: body.holderName,
      updatedAt: new Date().toISOString(),
    }
    // Como o serviço real: o que esperava cartão volta a ser agendado.
    payouts = payouts.map((p) =>
      p.status === 'AwaitingCard' || p.status === 'Failed'
        ? { ...p, status: 'Scheduled', scheduledFor: new Date().toISOString(), failureReason: null }
        : p,
    )
    return HttpResponse.json(card)
  }),

  http.get('/api/exchange-rates/convert', ({ request }) => {
    const url = new URL(request.url)
    const amount = Number(url.searchParams.get('amount'))
    const from = url.searchParams.get('from') ?? ''
    const to = url.searchParams.get('to') ?? ''
    if (!(from in usdPerUnit) || !(to in usdPerUnit)) {
      return HttpResponse.json({ status: 400, errors: { to: ['Currency is not supported.'] } }, { status: 400 })
    }
    const rate = Math.round((usdPerUnit[from] / usdPerUnit[to]) * 1e7) / 1e7
    return HttpResponse.json({
      amount,
      from,
      to,
      rate,
      convertedAmount: Math.round(amount * rate * 100) / 100,
      rateDate: '2026-11-03',
      source: 'mastercard',
    })
  }),
]
