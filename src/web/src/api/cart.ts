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

export interface CheckoutResult {
  checkoutId: string
  orderId: string
}

export const cartApi = {
  get: (token: string, signal?: AbortSignal) => apiFetch<Cart>('/api/cart', { token, signal }),

  add: (productId: string, quantity: number, token: string) =>
    apiFetch<Cart>('/api/cart/items', { method: 'POST', body: { productId, quantity }, token }),

  changeQuantity: (productId: string, quantity: number, token: string) =>
    apiFetch<Cart>(`/api/cart/items/${encodeURIComponent(productId)}`, { method: 'PUT', body: { quantity }, token }),

  remove: (productId: string, token: string) =>
    apiFetch<Cart>(`/api/cart/items/${encodeURIComponent(productId)}`, { method: 'DELETE', token }),

  /** 202: o pedido nasce de forma assíncrona no Orders; o carrinho já volta vazio. */
  checkout: (token: string) => apiFetch<CheckoutResult>('/api/cart/checkout', { method: 'POST', token }),
}

/** Mensagem em português para um erro ao finalizar o pedido. */
export function checkoutErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 400 && error.fieldErrors.cart) {
      return 'Seu carrinho está vazio.'
    }
    if (error.status === 409) {
      return 'Seu carrinho mudou em outra aba. Recarregue a página e tente de novo.'
    }
    if (error.status === 503) {
      return 'Não foi possível finalizar o pedido agora. Tente de novo em instantes.'
    }
  }
  return 'Não foi possível finalizar o pedido. Tente novamente.'
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
