import { apiFetch } from './client'

export type PayoutStatus = 'AwaitingDelivery' | 'AwaitingCard' | 'Scheduled' | 'Sent' | 'Failed' | 'Canceled'

/** Repasse de um vendedor por um pedido: bruto, taxa da plataforma e líquido (o que ele recebe). */
export interface Payout {
  id: string
  orderId: string
  status: PayoutStatus
  gross: number
  fee: number
  net: number
  currency: string
  createdAt: string
  deliveredAt: string | null
  scheduledFor: string | null
  sentAt: string | null
  providerReference: string | null
  failureReason: string | null
}

/** O cartão de recebimento como a API devolve: nunca o número completo. */
export interface PayoutCard {
  brand: 'Mastercard'
  last4: string
  expiryMonth: number
  expiryYear: number
  holderName: string
  updatedAt: string
}

export interface PayoutTotals {
  currency: string
  pending: number
  sent: number
}

export interface SellerPayouts {
  card: PayoutCard | null
  totals: PayoutTotals[]
  payouts: Payout[]
}

export interface RegisterCardInput {
  cardNumber: string
  expiryMonth: number | null
  expiryYear: number | null
  holderName: string
}

export interface Conversion {
  amount: number
  from: string
  to: string
  rate: number
  convertedAmount: number
  rateDate: string
  /** mastercard | simulated | identity */
  source: string
}

export const payoutsApi = {
  /** Cartão, totais e repasses do vendedor logado (mais recentes primeiro). */
  list: (token: string, signal?: AbortSignal) => apiFetch<SellerPayouts>('/api/payouts', { token, signal }),

  /** Cadastra ou troca o cartão Mastercard de recebimento. */
  registerCard: (input: RegisterCardInput, token: string) =>
    apiFetch<PayoutCard>('/api/payouts/card', { method: 'PUT', body: input, token }),
}

export const exchangeRatesApi = {
  /** Preço convertido com a cotação da Mastercard (informativo). */
  convert: (amount: number, from: string, to: string, signal?: AbortSignal) =>
    apiFetch<Conversion>(
      `/api/exchange-rates/convert?amount=${encodeURIComponent(amount)}&from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      { signal },
    ),
}

export const payoutStatusLabel: Record<PayoutStatus, string> = {
  AwaitingDelivery: 'Aguardando entrega',
  AwaitingCard: 'Cadastre o cartão',
  Scheduled: 'Agendado',
  Sent: 'Pago',
  Failed: 'Recusado',
  Canceled: 'Cancelado',
}

/** Moedas oferecidas no conversor da página do produto. */
export const conversionCurrencies = ['USD', 'EUR', 'GBP', 'ARS'] as const
