import { http, HttpResponse } from 'msw'
import type { Cart, CartItem } from '../api/cart'
import { placeFakeOrder } from './orders'
import type { Product } from '../api/products'
import { findFakeProduct } from './products'
import { userIdFromAuthorization } from './tokens'

/** Carrinhos da API falsa por usuário, com as mesmas regras da API real (estoque, próprio produto). */
let carts = new Map<string, CartItem[]>()

export function resetFakeCarts() {
  carts = new Map()
}

/** Coloca itens direto no carrinho falso de um usuário (para montar cenários). */
export function seedFakeCart(userId: string, items: CartItem[]) {
  carts.set(userId, items.map((i) => ({ ...i })))
}

/** Item de carrinho de um produto de exemplo, com subtotal calculado. */
export const cartItem = (product: Product, quantity: number): CartItem => ({
  productId: product.id,
  sellerId: product.sellerId,
  productName: product.name,
  unitPrice: product.price,
  currency: product.currency,
  quantity,
  subtotal: Math.round(product.price * quantity * 100) / 100,
})

const toCart = (items: CartItem[]): Cart => ({
  items,
  totalQuantity: items.reduce((sum, i) => sum + i.quantity, 0),
  total: Math.round(items.reduce((sum, i) => sum + i.subtotal, 0) * 100) / 100,
  currency: items[0]?.currency ?? 'BRL',
})

const validation = (field: string, message: string) =>
  HttpResponse.json({ status: 400, errors: { [field]: [message] } }, { status: 400 })

const notFound = (code: string) => HttpResponse.json({ status: 404, title: 'Not found.', code }, { status: 404 })

function stockError(stock: number, quantity: number) {
  if (stock <= 0) return validation('quantity', 'This product is out of stock.')
  if (quantity > stock) return validation('quantity', `Only ${stock} unit(s) in stock.`)
  return null
}

export const cartHandlers = [
  http.get('/api/cart', ({ request }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    return userId ? HttpResponse.json(toCart(carts.get(userId) ?? [])) : new HttpResponse(null, { status: 401 })
  }),

  http.post('/api/cart/items', async ({ request }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    if (!userId) return new HttpResponse(null, { status: 401 })
    const body = (await request.json()) as { productId: string; quantity?: number }
    const product = findFakeProduct(body.productId)
    if (!product) return notFound('product.not_found')
    if (product.sellerId === userId) return validation('productId', 'You cannot buy your own product.')

    const items = carts.get(userId) ?? []
    const existing = items.find((i) => i.productId === product.id)
    const quantity = (existing?.quantity ?? 0) + (body.quantity ?? 1)
    const error = stockError(product.stock, quantity)
    if (error) return error

    const item: CartItem = {
      productId: product.id,
      sellerId: product.sellerId,
      productName: product.name,
      unitPrice: product.price,
      currency: product.currency,
      quantity,
      subtotal: Math.round(product.price * quantity * 100) / 100,
    }
    carts.set(userId, existing ? items.map((i) => (i.productId === product.id ? item : i)) : [...items, item])
    return HttpResponse.json(toCart(carts.get(userId)!))
  }),

  http.put('/api/cart/items/:productId', async ({ request, params }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    if (!userId) return new HttpResponse(null, { status: 401 })
    const items = carts.get(userId) ?? []
    const item = items.find((i) => i.productId === params.productId)
    if (!item) return notFound('cart.item_not_found')
    const { quantity } = (await request.json()) as { quantity: number }
    const error = stockError(findFakeProduct(item.productId)?.stock ?? 0, quantity)
    if (error) return error

    const updated = { ...item, quantity, subtotal: Math.round(item.unitPrice * quantity * 100) / 100 }
    carts.set(userId, items.map((i) => (i.productId === item.productId ? updated : i)))
    return HttpResponse.json(toCart(carts.get(userId)!))
  }),

  http.delete('/api/cart/items/:productId', ({ request, params }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    if (!userId) return new HttpResponse(null, { status: 401 })
    const items = carts.get(userId) ?? []
    if (!items.some((i) => i.productId === params.productId)) return notFound('cart.item_not_found')
    carts.set(userId, items.filter((i) => i.productId !== params.productId))
    return HttpResponse.json(toCart(carts.get(userId)!))
  }),

  http.post('/api/cart/checkout', ({ request }) => {
    const userId = userIdFromAuthorization(request.headers.get('Authorization'))
    if (!userId) return new HttpResponse(null, { status: 401 })
    const items = carts.get(userId) ?? []
    if (items.length === 0) return validation('cart', 'The cart is empty.')
    const orderId = placeFakeOrder(userId, items)
    carts.set(userId, [])
    return HttpResponse.json({ checkoutId: orderId, orderId }, { status: 202 })
  }),
]
