import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { ApiError } from '../api/client'
import { usersApi } from '../api/users'
import { AuthContext, type AuthContextValue, type AuthState } from './context'
import { clearSession, loadSession, saveSession } from './session'

const anonymous: AuthState = { status: 'anonymous', user: null, token: null }

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(() =>
    loadSession() ? { status: 'loading', user: null, token: null } : anonymous,
  )

  // Ao abrir a página com uma sessão salva, confirma com a API quem é o usuário.
  useEffect(() => {
    const session = loadSession()
    if (!session) {
      return
    }

    const controller = new AbortController()
    usersApi
      .me(session.accessToken, controller.signal)
      .then((user) => setState({ status: 'authenticated', user, token: session.accessToken }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ApiError && error.status === 401) {
          clearSession()
        }
        setState(anonymous)
      })

    return () => controller.abort()
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const token = await usersApi.login(email, password)
    saveSession({ accessToken: token.accessToken, expiresAt: token.expiresAt })
    const user = await usersApi.me(token.accessToken)
    setState({ status: 'authenticated', user, token: token.accessToken })
  }, [])

  const logout = useCallback(() => {
    clearSession()
    setState({ ...anonymous, loggedOut: true })
  }, [])

  const value = useMemo<AuthContextValue>(() => ({ state, login, logout }), [state, login, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
