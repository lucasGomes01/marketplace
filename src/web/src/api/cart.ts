import { ApiError, apiFetch } from './client'
import { translateFieldErrors } from './messages'

export interface CartItem {
  productId: string
  sellerId: string
  productName: string
  unitPrice: number
  currency: string
  quantity: number
  subtotal: number
}

export interface Cart {
  items: CartItem[]
  totalQuantity: number
  total: number
  currency: string
}

export const cartApi = {
  get: (token: string, signal?: AbortSignal) => apiFetch<Cart>('/api/cart', { token, signal }),

  add: (productId: string, quantity: number, token: string) =>
    apiFetch<Cart>('/api/cart/items', { method: 'POST', body: { productId, quantity }, token }),

  changeQuantity: (productId: string, quantity: number, token: string) =>
    apiFetch<Cart>(`/api/cart/items/${encodeURIComponent(productId)}`, { method: 'PUT', body: { quantity }, token }),

  remove: (productId: string, token: string) =>
    apiFetch<Cart>(`/api/cart/items/${encodeURIComponent(productId)}`, { method: 'DELETE', token }),
}

/** Mensagem em português para um erro de operação no carrinho. */
export function cartErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldMessages = Object.values(translateFieldErrors(error.fieldErrors, 'cart'))
    if (fieldMessages.length > 0) {
      return fieldMessages[0]
    }
    if (error.status === 503) {
      return 'O catálogo está indisponível agora. Tente de novo em instantes.'
    }
    if (error.status === 404) {
      return 'Este produto não está mais disponível.'
    }
    if (error.status === 409) {
      return 'Seu carrinho mudou em outra aba. Recarregue a página e tente de novo.'
    }
  }
  return 'Não foi possível atualizar o carrinho. Tente novamente.'
}
