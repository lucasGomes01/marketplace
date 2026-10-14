import { createContext, useContext } from 'react'
import type { Cart } from '../api/cart'

export interface CartContextValue {
  /** Carrinho da pessoa logada; null sem login ou enquanto carrega. */
  cart: Cart | null
  /** Quantidade total de itens (0 sem login). */
  count: number
  /** As operações lançam ApiError; use cartErrorMessage para mostrar. */
  add: (productId: string, quantity?: number) => Promise<void>
  changeQuantity: (productId: string, quantity: number) => Promise<void>
  remove: (productId: string) => Promise<void>
  /** Finaliza o pedido e devolve o id dele; o carrinho fica vazio. */
  checkout: () => Promise<string>
}

export const CartContext = createContext<CartContextValue | null>(null)

export function useCart(): CartContextValue {
  const value = useContext(CartContext)
  if (!value) {
    throw new Error('useCart must be used inside <CartProvider>.')
  }
  return value
}
