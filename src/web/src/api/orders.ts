import { apiFetch } from './client'

export type OrderStatus = 'Pending' | 'Confirmed' | 'Rejected'

export interface OrderItem {
  productId: string
  sellerId: string
  productName: string
  unitPrice: number
  currency: string
  quantity: number
  subtotal: number
}

export interface Order {
  id: string
  status: OrderStatus
  rejectionReason: string | null
  items: OrderItem[]
  itemCount: number
  total: number
  currency: string
  createdAt: string
  updatedAt: string
}

export const ordersApi = {
  /** Pedidos da pessoa logada, mais recentes primeiro. */
  list: (token: string, signal?: AbortSignal) => apiFetch<Order[]>('/api/orders', { token, signal }),

  /** 404 também enquanto o pedido ainda não nasceu (o checkout é assíncrono). */
  get: (id: string, token: string, signal?: AbortSignal) =>
    apiFetch<Order>(`/api/orders/${encodeURIComponent(id)}`, { token, signal }),
}

/** Como a tela do pedido acompanha o status (mutável só para os testes acelerarem). */
export const orderPolling = { intervalMs: 1500, maxAttempts: 40 }

export const orderStatusLabel: Record<OrderStatus, string> = {
  Pending: 'Processando',
  Confirmed: 'Confirmado',
  Rejected: 'Recusado',
}
