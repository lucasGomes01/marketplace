import { http, HttpResponse } from 'msw'
import type { CartItem } from '../api/cart'
import type { Order } from '../api/orders'
import { userIdFromAuthorization } from './tokens'

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

function newOrder(id: string, items: CartItem[], createdAt: string): Order {
  return {
    id,
    status: 'Pending',
    rejectionReason: null,
    items: items.map((i) => ({ ...i })),
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
]
