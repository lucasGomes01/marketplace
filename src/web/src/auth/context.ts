import { createContext, useContext } from 'react'
import type { User } from '../api/users'

export type AuthState =
  | { status: 'loading'; user: null; token: null }
  // loggedOut: a pessoa clicou em Sair (rotas protegidas mandam para a home, não para o login).
  | { status: 'anonymous'; user: null; token: null; loggedOut?: boolean }
  | { status: 'authenticated'; user: User; token: string }

export interface AuthContextValue {
  state: AuthState
  login: (email: string, password: string) => Promise<void>
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) {
    throw new Error('useAuth must be used inside <AuthProvider>.')
  }
  return value
}
