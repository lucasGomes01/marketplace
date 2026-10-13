import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuth } from './context'

/** Rotas filhas só para quem está logado; os demais vão para o login e voltam depois. */
export function RequireAuth() {
  const { state } = useAuth()
  const location = useLocation()

  if (state.status === 'loading') {
    return <p className="muted" role="status">Carregando…</p>
  }
  if (state.status === 'anonymous') {
    return state.loggedOut ? (
      <Navigate to="/" replace />
    ) : (
      <Navigate to="/entrar" replace state={{ from: location.pathname }} />
    )
  }
  return <Outlet />
}
