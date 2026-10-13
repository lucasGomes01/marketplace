import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { useAuth } from '../auth/context'

export function Layout() {
  const { state, logout } = useAuth()
  const navigate = useNavigate()

  function handleLogout() {
    logout()
    navigate('/')
  }

  return (
    <>
      <header className="site-header">
        <div className="container site-header__inner">
          <Link to="/" className="brand">
            <span className="brand__mark" aria-hidden="true">◆</span> Marketplace
          </Link>

          <nav className="site-nav" aria-label="Conta">
            {state.status === 'authenticated' ? (
              <>
                <NavLink to="/minha-conta" className="site-nav__user">
                  {state.user.name}
                </NavLink>
                <button type="button" className="button button--ghost" onClick={handleLogout}>
                  Sair
                </button>
              </>
            ) : state.status === 'anonymous' ? (
              <>
                <NavLink to="/entrar" className="button button--ghost">
                  Entrar
                </NavLink>
                <NavLink to="/cadastro" className="button button--primary">
                  Criar conta
                </NavLink>
              </>
            ) : null}
          </nav>
        </div>
      </header>

      <main className="container site-main">
        <Outlet />
      </main>
    </>
  )
}
