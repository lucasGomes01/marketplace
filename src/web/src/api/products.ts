import { apiFetch } from './client'

export type ProductCondition = 'New' | 'Used' | 'Refurbished'

export interface ProductAttribute {
  name: string
  value: string
}

export interface Product {
  id: string
  sellerId: string
  name: string
  description: string | null
  price: number
  currency: string
  stock: number
  createdAt: string
  // Detalhes do anúncio (T-028). Opcionais no tipo: a página funciona com um produto só com o essencial.
  category?: string
  brand?: string | null
  sku?: string | null
  condition?: ProductCondition
  imageUrls?: string[]
  originalPrice?: number | null
  weightGrams?: number | null
  lengthCm?: number | null
  widthCm?: number | null
  heightCm?: number | null
  warrantyMonths?: number | null
  freeShipping?: boolean
  attributes?: ProductAttribute[]
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface NewProduct {
  name: string
  description?: string
  price: number
  stock: number
  category?: string
  brand?: string
  sku?: string
  condition?: ProductCondition
  imageUrls?: string[]
  originalPrice?: number
  weightGrams?: number
  lengthCm?: number
  widthCm?: number
  heightCm?: number
  warrantyMonths?: number
  freeShipping?: boolean
  attributes?: ProductAttribute[]
}

export type ProductSort = 'newest' | 'price_asc' | 'price_desc' | 'name'

export interface ProductFilter {
  search?: string
  category?: string
  condition?: ProductCondition
  minPrice?: number
  maxPrice?: number
  freeShipping?: boolean
  sort?: ProductSort
}

function listQuery(page: number, pageSize: number, filter: ProductFilter): string {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  if (filter.search) params.set('q', filter.search)
  if (filter.category) params.set('category', filter.category)
  if (filter.condition) params.set('condition', filter.condition)
  if (filter.minPrice !== undefined) params.set('minPrice', String(filter.minPrice))
  if (filter.maxPrice !== undefined) params.set('maxPrice', String(filter.maxPrice))
  if (filter.freeShipping) params.set('freeShipping', 'true')
  if (filter.sort && filter.sort !== 'newest') params.set('sort', filter.sort)
  return params.toString()
}

export const productsApi = {
  list: (page: number, pageSize: number, signal?: AbortSignal, filter: ProductFilter = {}) =>
    apiFetch<Page<Product>>(`/api/products?${listQuery(page, pageSize, filter)}`, { signal }),

  get: (id: string, signal?: AbortSignal) => apiFetch<Product>(`/api/products/${encodeURIComponent(id)}`, { signal }),

  create: (product: NewProduct, token: string) =>
    apiFetch<Product>('/api/products', { method: 'POST', body: product, token }),
}

/** Texto do estoque para quem compra. */
export function stockLabel(stock: number): string {
  if (stock <= 0) {
    return 'Esgotado'
  }
  if (stock === 1) {
    return 'Última unidade'
  }
  if (stock <= 3) {
    return `Últimas ${stock} unidades`
  }
  return `${stock} em estoque`
}

/** Percentual de desconto arredondado (ex.: 20), ou null quando não há preço "de" maior que o preço. */
export function discountPercent(product: Pick<Product, 'price' | 'originalPrice'>): number | null {
  const original = product.originalPrice
  if (!original || original <= product.price) {
    return null
  }
  const percent = Math.round((1 - product.price / original) * 100)
  return percent > 0 ? percent : null
}
