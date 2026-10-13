import { http, HttpResponse } from 'msw'
import type { Product } from '../api/products'
import { buyerToken } from './tokens'

/** Produtos da API falsa (mais recente primeiro, como a API real). */
export const sampleProducts: Product[] = [
  {
    id: 'p3-0000-0000-0000-000000000003',
    sellerId: 's1',
    name: 'Trena a laser 40m',
    description: null,
    price: 249,
    currency: 'BRL',
    stock: 0,
    createdAt: '2026-10-13T15:03:00Z',
  },
  {
    id: 'p2-0000-0000-0000-000000000002',
    sellerId: 's1',
    name: 'Jogo de chaves combinadas',
    description: '12 peças, aço cromo-vanádio',
    price: 89.5,
    currency: 'BRL',
    stock: 2,
    createdAt: '2026-10-13T15:02:00Z',
  },
  {
    id: 'p1-0000-0000-0000-000000000001',
    sellerId: 's1',
    name: 'Furadeira de impacto 700W',
    description: 'Com maleta e 3 brocas',
    price: 199.9,
    currency: 'BRL',
    stock: 5,
    createdAt: '2026-10-13T15:01:00Z',
  },
]

let products: Product[] = []

export function resetFakeProducts(items: Product[] = sampleProducts) {
  products = items.map((p) => ({ ...p }))
}
resetFakeProducts()

/** N produtos genéricos, para testar paginação. */
export const manyProducts = (count: number): Product[] =>
  Array.from({ length: count }, (_, i) => ({
    ...sampleProducts[2],
    id: `gen-${i + 1}`,
    name: `Produto ${i + 1}`,
  }))

export const productHandlers = [
  http.get('/api/products', ({ request }) => {
    const url = new URL(request.url)
    const page = Number(url.searchParams.get('page') ?? 1)
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20)
    const items = products.slice((page - 1) * pageSize, page * pageSize)
    return HttpResponse.json({ items, page, pageSize, totalCount: products.length })
  }),

  http.get('/api/products/:id', ({ params }) => {
    const product = products.find((p) => p.id === params.id)
    return product
      ? HttpResponse.json(product)
      : HttpResponse.json({ status: 404, title: 'Product not found.', code: 'product.not_found' }, { status: 404 })
  }),

  http.post('/api/products', async ({ request }) => {
    const auth = request.headers.get('Authorization')
    if (!auth) {
      return new HttpResponse(null, { status: 401 })
    }
    if (auth === `Bearer ${buyerToken}`) {
      return new HttpResponse(null, { status: 403 })
    }
    const body = (await request.json()) as { name: string; description?: string; price: number; stock: number }
    const product: Product = {
      id: `new-${products.length + 1}`,
      sellerId: 's1',
      name: body.name.trim(),
      description: body.description?.trim() || null,
      price: body.price,
      currency: 'BRL',
      stock: body.stock,
      createdAt: new Date().toISOString(),
    }
    products.unshift(product)
    return HttpResponse.json(product, { status: 201 })
  }),
]
