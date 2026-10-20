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

/** Etapas do envio de um vendedor, nesta ordem. */
export const shipmentSteps = ['AwaitingPicking', 'Picked', 'Packed', 'Shipped', 'Delivered'] as const
export type ShipmentStatus = (typeof shipmentSteps)[number]

/** Envio dos itens de um vendedor; datas null enquanto a etapa não aconteceu. */
export interface Shipment {
  sellerId: string
  status: ShipmentStatus
  pickedAt: string | null
  packedAt: string | null
  shippedAt: string | null
  deliveredAt: string | null
}

export interface Order {
  id: string
  status: OrderStatus
  rejectionReason: string | null
  items: OrderItem[]
  shipments: Shipment[]
  itemCount: number
  total: number
  currency: string
  createdAt: string
  updatedAt: string
}

/** O que o vendedor vê: o envio dele, só com os itens dele. */
export interface SellerShipment extends Shipment {
  orderId: string
  items: OrderItem[]
  itemCount: number
  total: number
  currency: string
  orderedAt: string
}

export const ordersApi = {
  /** Pedidos da pessoa logada, mais recentes primeiro. */
  list: (token: string, signal?: AbortSignal) => apiFetch<Order[]>('/api/orders', { token, signal }),

  /** 404 também enquanto o pedido ainda não nasceu (o checkout é assíncrono). */
  get: (id: string, token: string, signal?: AbortSignal) =>
    apiFetch<Order>(`/api/orders/${encodeURIComponent(id)}`, { token, signal }),

  /** "Recebi": o comprador confirma a entrega do envio de um vendedor. */
  confirmDelivery: (orderId: string, sellerId: string, token: string) =>
    apiFetch<Order>(
      `/api/orders/${encodeURIComponent(orderId)}/shipments/${encodeURIComponent(sellerId)}/delivery`,
      { method: 'POST', token },
    ),
}

export const shipmentsApi = {
  /** Envios do vendedor logado em pedidos confirmados, mais recentes primeiro. */
  list: (token: string, signal?: AbortSignal) => apiFetch<SellerShipment[]>('/api/shipments', { token, signal }),

  advance: (orderId: string, status: ShipmentStatus, token: string) =>
    apiFetch<SellerShipment>(`/api/shipments/${encodeURIComponent(orderId)}/status`, {
      method: 'PUT',
      body: { status },
      token,
    }),
}

export const shipmentStatusLabel: Record<ShipmentStatus, string> = {
  AwaitingPicking: 'Aguardando separação',
  Picked: 'Separado',
  Packed: 'Embalado',
  Shipped: 'Enviado',
  Delivered: 'Entregue',
}

/** Próxima etapa que o vendedor pode marcar (Delivered é do comprador). */
export function nextSellerStep(status: ShipmentStatus): ShipmentStatus | null {
  const next = shipmentSteps[shipmentSteps.indexOf(status) + 1]
  return next && next !== 'Delivered' ? next : null
}

/** Quando o envio chegou à etapa (null se ainda não chegou). */
export function reachedAt(shipment: Shipment, status: ShipmentStatus): string | null {
  switch (status) {
    case 'Picked':
      return shipment.pickedAt
    case 'Packed':
      return shipment.packedAt
    case 'Shipped':
      return shipment.shippedAt
    case 'Delivered':
      return shipment.deliveredAt
    default:
      return null
  }
}

/**
 * Andamento da entrega de um pedido confirmado: a etapa de todos os envios, ou "Parcialmente <etapa mais avançada>"
 * quando estão em etapas diferentes. null antes da confirmação.
 */
export function deliverySummary(order: Order): string | null {
  if (order.status !== 'Confirmed' || order.shipments.length === 0) return null
  const positions = order.shipments.map((s) => shipmentSteps.indexOf(s.status))
  const max = Math.max(...positions)
  const label = shipmentStatusLabel[shipmentSteps[max]]
  return positions.every((p) => p === max) ? label : `Parcialmente ${label.toLowerCase()}`
}

/** Como a tela do pedido acompanha o status (mutável só para os testes acelerarem). */
export const orderPolling = { intervalMs: 1500, maxAttempts: 40 }

export const orderStatusLabel: Record<OrderStatus, string> = {
  Pending: 'Processando',
  Confirmed: 'Confirmado',
  Rejected: 'Recusado',
}
