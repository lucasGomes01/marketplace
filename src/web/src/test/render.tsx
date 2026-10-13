import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { AppRoutes } from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { CartProvider } from '../cart/CartProvider'
import { saveSession } from '../auth/session'
import { buyerToken, validToken } from './server'

interface Options {
  /**
   * Começa com uma sessão salva (como se a pessoa já tivesse feito login).
   * true = conta que compra e vende; 'buyer' = conta que só compra.
   */
  loggedIn?: boolean | 'buyer'
}

/** Renderiza a aplicação inteira numa rota, com a API falsa do MSW. */
export function renderApp(route = '/', { loggedIn = false }: Options = {}) {
  if (loggedIn) {
    saveSession({ accessToken: loggedIn === 'buyer' ? buyerToken : validToken, expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString() })
  }

  return {
    user: userEvent.setup(),
    ...render(
      <MemoryRouter initialEntries={[route]}>
        <AuthProvider>
          <CartProvider>
            <AppRoutes />
          </CartProvider>
        </AuthProvider>
      </MemoryRouter>,
    ),
  }
}
