import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { cartApi, type Cart } from '../api/cart'
import { useAuth } from '../auth/context'
import { CartContext, type CartContextValue } from './context'

/** Carrinho compartilhado entre as telas (cabeçalho, produto, carrinho). Recarrega quando a sessão muda. */
export function CartProvider({ children }: { children: ReactNode }) {
  const { state } = useAuth()
  const token = state.status === 'authenticated' ? state.token : null

  // Guarda de qual sessão é o carrinho: ao sair ou trocar de conta ele some sem setState no efeito.
  const [loaded, setLoaded] = useState<{ token: string; cart: Cart } | null>(null)
  const cart = token && loaded?.token === token ? loaded.cart : null

  useEffect(() => {
    if (!token) {
      return
    }
    const controller = new AbortController()
    cartApi
      .get(token, controller.signal)
      .then((result) => setLoaded({ token, cart: result }))
      .catch(() => {
        // Sem carrinho visível; as telas que dependem dele mostram o próprio erro ao operar.
      })
    return () => controller.abort()
  }, [token])

  const run = useCallback(
    async (operation: (token: string) => Promise<Cart>) => {
      if (!token) {
        throw new Error('Cart operations require an authenticated session.')
      }
      const result = await operation(token)
      setLoaded({ token, cart: result })
    },
    [token],
  )

  const value = useMemo<CartContextValue>(
    () => ({
      cart,
      count: cart?.totalQuantity ?? 0,
      add: (productId, quantity = 1) => run((t) => cartApi.add(productId, quantity, t)),
      changeQuantity: (productId, quantity) => run((t) => cartApi.changeQuantity(productId, quantity, t)),
      remove: (productId) => run((t) => cartApi.remove(productId, t)),
      checkout: async () => {
        let orderId = ''
        await run(async (t) => {
          orderId = (await cartApi.checkout(t)).orderId
          return { items: [], totalQuantity: 0, total: 0, currency: cart?.currency ?? 'BRL' }
        })
        return orderId
      },
    }),
    [cart, run],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}
