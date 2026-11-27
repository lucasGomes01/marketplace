import { http, HttpResponse } from 'msw'
import type { NewProduct, Product } from '../api/products'
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
    category: 'ferramentas',
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
    category: 'ferramentas',
    condition: 'Used',
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
    category: 'ferramentas',
    brand: 'Bosch',
    condition: 'New',
    imageUrls: [],
    originalPrice: 249.9,
    freeShipping: true,
    warrantyMonths: 12,
    weightGrams: 1800,
    lengthCm: 30,
    widthCm: 25,
    heightCm: 10,
    attributes: [{ name: 'Voltagem', value: '220V' }],
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

/** Produto atual da API falsa (o carrinho falso consulta aqui, como o Cart real consulta o Catalog). */
export const findFakeProduct = (id: string) => products.find((p) => p.id === id)

export const productHandlers = [
  http.get('/api/products', ({ request }) => {
    const url = new URL(request.url)
    const page = Number(url.searchParams.get('page') ?? 1)
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20)
    const q = url.searchParams.get('q')?.toLowerCase()
    const category = url.searchParams.get('category')
    const condition = url.searchParams.get('condition')
    const minPrice = url.searchParams.get('minPrice')
    const maxPrice = url.searchParams.get('maxPrice')
    const freeShipping = url.searchParams.get('freeShipping') === 'true'
    const sort = url.searchParams.get('sort')

    const matching = products.filter(
      (p) =>
        (!q || p.name.toLowerCase().includes(q) || (p.brand ?? '').toLowerCase().includes(q)) &&
        (!category || (p.category ?? 'outros') === category) &&
        (!condition || (p.condition ?? 'New') === condition) &&
        (minPrice === null || p.price >= Number(minPrice)) &&
        (maxPrice === null || p.price <= Number(maxPrice)) &&
        (!freeShipping || p.freeShipping === true),
    )
    if (sort === 'price_asc') matching.sort((a, b) => a.price - b.price)
    if (sort === 'price_desc') matching.sort((a, b) => b.price - a.price)
    if (sort === 'name') matching.sort((a, b) => a.name.localeCompare(b.name))

    const items = matching.slice((page - 1) * pageSize, page * pageSize)
    return HttpResponse.json({ items, page, pageSize, totalCount: matching.length })
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
    const body = (await request.json()) as NewProduct
    const product: Product = {
      ...body,
      id: `new-${products.length + 1}`,
      sellerId: 's1',
      name: body.name.trim(),
      description: body.description?.trim() || null,
      currency: 'BRL',
      createdAt: new Date().toISOString(),
      category: body.category ?? 'outros',
      condition: body.condition ?? 'New',
      imageUrls: body.imageUrls ?? [],
      attributes: body.attributes ?? [],
      freeShipping: body.freeShipping ?? false,
    }
    products.unshift(product)
    return HttpResponse.json(product, { status: 201 })
  }),
]
