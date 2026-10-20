import { http, HttpResponse } from 'msw'
import type { CartItem } from '../api/cart'
import { shipmentSteps, type Order, type SellerShipment, type Shipment, type ShipmentStatus } from '../api/orders'
import { isSellerAuthorization, userIdFromAuthorization } from './tokens'

/**
 * Pedidos da API falsa. Imita o fluxo assíncrono real: depois do checkout o pedido dá 404 por algumas consultas
 * (o Orders ainda não consumiu o evento), depois fica `Pending` e por fim vira o resultado combinado.
 */
interface FakeOrder {
  buyerId: string
  order: Order
  reads: number
  notFoundReads: number
  pendingReads: number
  outcome: { status: 'Confirmed' } | { status: 'Rejected'; reason: string } | { status: 'Pending' }
}

let orders = new Map<string, FakeOrder>()
let nextOutcome: FakeOrder['outcome'] = { status: 'Confirmed' }
let nextTiming = { notFoundReads: 1, pendingReads: 1 }

export function resetFakeOrders() {
  orders = new Map()
  nextOutcome = { status: 'Confirmed' }
  nextTiming = { notFoundReads: 1, pendingReads: 1 }
  conflictOnNextAdvance = false
}

/** Como o próximo pedido criado pelo checkout vai terminar (e quantas consultas leva para isso). */
export function nextFakeOrderWill(
  outcome: FakeOrder['outcome'],
  timing: Partial<typeof nextTiming> = {},
) {
  nextOutcome = outcome
  nextTiming = { ...nextTiming, ...timing }
}

const money = (value: number) => Math.round(value * 100) / 100

/** Um envio por vendedor, na ordem dos itens (igual ao Orders). */
export function newShipments(items: { sellerId: string }[]): Shipment[] {
  const sellers = [...new Set(items.map((i) => i.sellerId))]
  return sellers.map((sellerId) => ({
    sellerId,
    status: 'AwaitingPicking',
    pickedAt: null,
    packedAt: null,
    shippedAt: null,
    deliveredAt: null,
  }))
}

/** Envio já numa etapa, com as datas das etapas anteriores preenchidas. */
export function shipmentAt(sellerId: string, status: ShipmentStatus, at = '2026-10-15T10:00:00Z'): Shipment {
  const reached = (step: ShipmentStatus) => (shipmentSteps.indexOf(status) >= shipmentSteps.indexOf(step) ? at : null)
  return {
    sellerId,
    status,
    pickedAt: reached('Picked'),
    packedAt: reached('Packed'),
    shippedAt: reached('Shipped'),
    deliveredAt: reached('Delivered'),
  }
}

function newOrder(id: string, items: CartItem[], createdAt: string): Order {
  return {
    id,
    status: 'Pending',
    rejectionReason: null,
    items: items.map((i) => ({ ...i })),
    shipments: newShipments(items),
    itemCount: items.reduce((sum, i) => sum + i.quantity, 0),
    total: money(items.reduce((sum, i) => sum + i.subtotal, 0)),
    currency: items[0]?.currency ?? 'BRL',
    createdAt,
    updatedAt: createdAt,
  }
}

/** Chamado pelo checkout falso: registra o pedido que "vai nascer". */
export function placeFakeOrder(buyerId: string, items: CartItem[]): string {
  const id = crypto.randomUUID()
  orders.set(id, {
    buyerId,
    order: newOrder(id, items, new Date().toISOString()),
    reads: 0,
    ...nextTiming,
    outcome: nextOutcome,
  })
  return id
}

/** Pedido já existente e resolvido (para montar o histórico). */
export function seedFakeOrder(buyerId: string, items: CartItem[], fields: Partial<Order>): Order {
  const order = { ...newOrder(fields.id ?? crypto.randomUUID(), items, fields.createdAt ?? new Date().toISOString()), ...fields }
  orders.set(order.id, { buyerId, order, reads: 0, notFoundReads: 0, pendingReads: 0, outcome: { status: order.status } as FakeOrder['outcome'] })
  return order
}

/** Avança o pedido a cada consulta; undefined enquanto ele "ainda não existe". */
function read(entry: FakeOrder): Order | undefined {
  entry.reads++
  if (entry.reads <= entry.notFoundReads) return undefined
  if (entry.reads > entry.notFoundReads + entry.pendingReads && entry.order.status === 'Pending') {
    const { outcome } = entry
    entry.order = {
      ...entry.order,
      status: outcome.status,
      rejectionReason: outcome.status === 'Rejected' ? outcome.reason : null,
      updatedAt: new Date().toISOString(),
    }
  }
  return entry.order
}

const notFound = () => HttpResponse.json({ status: 404, title: 'Order not found.', code: 'order.not_found' }, { status: 404 })

const problem = (status: number, code: string) => HttpResponse.json({ status, title: code, code }, { status })

const stamp: Record<ShipmentStatus, keyof Shipment | null> = {
  AwaitingPicking: null,
  Picked: 'pickedAt',
  Packed: 'packedAt',
  Shipped: 'shippedAt',
  Delivered: 'deliveredAt',
}

/** Aplica a mesma regra do Orders: só a próxima etapa; repetir a atual é aceito. */
function moveShipment(entry: FakeOrder, sellerId: string, to: ShipmentStatus): 'ok' | 'not-found' | 'invalid' {
  const shipment = entry.order.shipments.find((s) => s.sellerId === sellerId)
  if (!shipment) return 'not-found'
  const current = shipmentSteps.indexOf(shipment.status)
  const target = shipmentSteps.indexOf(to)
  if (entry.order.status !== 'Confirmed' || (target !== current && target !== current + 1)) return 'invalid'
  if (target === current + 1) {
    const updated = { ...shipment, status: to, [stamp[to]!]: new Date().toISOString() }
    entry.order = { ...entry.order, shipments: entry.order.shipments.map((s) => (s === shipment ? updated : s)) }
  }
  return 'ok'
}

function sellerView(order: Order, sellerId: string): SellerShipment {
  const items = order.items.filter((i) => i.sellerId === sellerId)
  return {
    ...order.shipments.find((s) => s.sellerId === sellerId)!,
    orderId: order.id,
    items,
    itemCount: items.reduce((sum, i) => sum + i.quantity, 0),
    total: money(items.reduce((sum, i) => sum + i.subtotal, 0)),
    currency: order.currency,
    orderedAt: order.createdAt,
  }
}

/** Próxima resposta do PUT de envio vira conflito (outra aba mudou o envio). */
let conflictOnNextAdvance = false
export function nextShipmentAdvanceConflicts() {
  conflictOnNextAdvance = true
}

export const orderHandlers = [
  http.get('/api/orders', ({ request }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    if (!userId) return new HttpResponse(null, { status: 401 })
    const mine = [...orders.values()]
      .filter((o) => o.buyerId === userId)
      .map(read)
      .filter((o): o is Order => o !== undefined)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    return HttpResponse.json(mine)
  }),

  http.get('/api/orders/:id', ({ request, params }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    if (!userId) return new HttpResponse(null, { status: 401 })
    const entry = orders.get(String(params.id))
    if (!entry || entry.buyerId !== userId) return notFound()
    const order = read(entry)
    return order ? HttpResponse.json(order) : notFound()
  }),

  http.post('/api/orders/:id/shipments/:sellerId/delivery', ({ request, params }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    if (!userId) return new HttpResponse(null, { status: 401 })
    const entry = orders.get(String(params.id))
    if (!entry || entry.buyerId !== userId) return problem(404, 'shipment.not_found')
    const result = moveShipment(entry, String(params.sellerId), 'Delivered')
    if (result === 'not-found') return problem(404, 'shipment.not_found')
    if (result === 'invalid') return problem(409, 'shipment.invalid_transition')
    return HttpResponse.json(entry.order)
  }),

  http.get('/api/shipments', ({ request }) => {
    const authorization = request.headers.get('Authorization')
    const userId = userIdFromAuthorization(authorization)
    if (!userId) return new HttpResponse(null, { status: 401 })
    if (!isSellerAuthorization(authorization)) return new HttpResponse(null, { status: 403 })
    const mine = [...orders.values()]
      .map((entry) => entry.order)
      .filter((o) => o.status === 'Confirmed' && o.shipments.some((s) => s.sellerId === userId))
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
      .map((o) => sellerView(o, userId))
    return HttpResponse.json(mine)
  }),

  http.put('/api/shipments/:orderId/status', async ({ request, params }) => {
    const authorization = request.headers.get('Authorization')
    const userId = userIdFromAuthorization(authorization)
    if (!userId) return new HttpResponse(null, { status: 401 })
    if (!isSellerAuthorization(authorization)) return new HttpResponse(null, { status: 403 })
    if (conflictOnNextAdvance) {
      conflictOnNextAdvance = false
      return problem(409, 'shipment.conflict')
    }
    const { status } = (await request.json()) as { status: ShipmentStatus }
    const entry = orders.get(String(params.orderId))
    if (!entry) return problem(404, 'shipment.not_found')
    if (status === 'Delivered') return problem(409, 'shipment.invalid_transition')
    const result = moveShipment(entry, userId, status)
    if (result === 'not-found') return problem(404, 'shipment.not_found')
    if (result === 'invalid') return problem(409, 'shipment.invalid_transition')
    return HttpResponse.json(sellerView(entry.order, userId))
  }),
]
