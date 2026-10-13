import { apiFetch } from './client'

export interface Product {
  id: string
  sellerId: string
  name: string
  description: string | null
  price: number
  currency: string
  stock: number
  createdAt: string
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
}

export const productsApi = {
  list: (page: number, pageSize: number, signal?: AbortSignal) =>
    apiFetch<Page<Product>>(`/api/products?page=${page}&pageSize=${pageSize}`, { signal }),

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
