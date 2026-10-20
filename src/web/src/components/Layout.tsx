import { Link, NavLink, Outlet, useNavigate, useSearchParams } from 'react-router'
import { isSeller } from '../api/users'
import { useAuth } from '../auth/context'
import { useCart } from '../cart/context'
import { CATEGORIES } from '../catalog/categories'
import { Icon } from './Icon'
import { SearchBar } from './SearchBar'

/** Categorias com atalho na barra abaixo do cabeçalho (as demais ficam nos filtros da vitrine). */
const NAV_CATEGORIES = CATEGORIES.filter((c) =>
  ['eletronicos', 'celulares', 'informatica', 'casa', 'eletrodomesticos', 'ferramentas', 'moda', 'esportes'].includes(
    c.code,
  ),
)

const CURRENT_YEAR = new Date().getFullYear()

export function Layout() {
  const { state, logout } = useAuth()
  const { count } = useCart()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const activeCategory = searchParams.get('categoria')

  function handleLogout() {
    logout()
    navigate('/')
  }

  return (
    <div className="app-shell">
      <a href="#conteudo" className="skip-link">
        Pular para o conteúdo
      </a>
      <header className="site-header">
        <div className="site-header__bar">
          <div className="container site-header__inner">
            <Link to="/" className="brand">
              <span className="brand__name">Marketplace</span>
              <span className="brand__dot" aria-hidden="true">
                .
              </span>
            </Link>

            <SearchBar />

            <nav className="site-nav" aria-label="Conta">
              {state.status === 'authenticated' ? (
                <>
                  <NavLink to="/vender/novo" className="site-nav__link">
                    Vender
                  </NavLink>
                  {isSeller(state.user) ? (
                    <NavLink to="/vender/envios" className="site-nav__link">
                      Despachar
                    </NavLink>
                  ) : null}
                  <NavLink to="/pedidos" className="site-nav__link">
                    Pedidos
                  </NavLink>
                  <NavLink to="/minha-conta" className="site-nav__user" title={state.user.name}>
                    <Icon name="user" size={18} />
                    <span className="site-nav__user-name">{state.user.name}</span>
                  </NavLink>
                  <button type="button" className="button button--ghost button--small" onClick={handleLogout}>
                    Sair
                  </button>
                  <NavLink to="/carrinho" className="site-nav__cart" aria-label={`Carrinho (${count})`}>
                    <Icon name="cart" size={22} />
                    <span className="site-nav__cart-label">Carrinho</span>
                    <span className="site-nav__cart-count" aria-hidden="true">
                      {count}
                    </span>
                  </NavLink>
                </>
              ) : state.status === 'anonymous' ? (
                <>
                  <NavLink to="/entrar" className="button button--ghost button--small">
                    Entrar
                  </NavLink>
                  <NavLink to="/cadastro" className="button button--primary button--small">
                    Criar conta
                  </NavLink>
                </>
              ) : null}
            </nav>
          </div>
        </div>

        <nav className="category-bar" aria-label="Categorias">
          <div className="container category-bar__inner">
            <Link to="/" className={`category-bar__link${activeCategory ? '' : ' category-bar__link--all'}`}>
              Todas as categorias
            </Link>
            {NAV_CATEGORIES.map((category) => (
              <Link
                key={category.code}
                to={`/?categoria=${category.code}`}
                className={`category-bar__link${activeCategory === category.code ? ' category-bar__link--active' : ''}`}
                aria-current={activeCategory === category.code ? 'page' : undefined}
              >
                {category.label}
              </Link>
            ))}
            <Link to="/?frete=1" className="category-bar__link category-bar__link--highlight">
              Frete grátis
            </Link>
          </div>
        </nav>
      </header>

      <main id="conteudo" className="container site-main">
        <Outlet />
      </main>

      <SiteFooter />
    </div>
  )
}

function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="container">
        <div className="site-footer__columns">
          <div className="site-footer__about">
            <Link to="/" className="brand">
              <span>Marketplace</span>
              <span className="brand__dot" aria-hidden="true">
                .
              </span>
            </Link>
            <p className="muted">
              Compre e venda direto de quem faz. Produtos novos, usados e recondicionados de vendedores de todo o
              Brasil.
            </p>
          </div>
          <nav aria-label="Comprar">
            <h2>Comprar</h2>
            {/* Sem <ul>: as telas de pedidos e envios procuram os itens da lista delas por "listitem". */}
            <div className="site-footer__links">
              <Link to="/">Todos os produtos</Link>
              <Link to="/?ordem=price_asc">Menores preços</Link>
              <Link to="/pedidos">Meus pedidos</Link>
            </div>
          </nav>
          <nav aria-label="Vender">
            <h2>Vender</h2>
            <div className="site-footer__links">
              <Link to="/vender/novo">Anunciar produto</Link>
              <Link to="/vender/envios">Envios a preparar</Link>
            </div>
          </nav>
          <nav aria-label="Sua conta">
            <h2>Sua conta</h2>
            <div className="site-footer__links">
              <Link to="/minha-conta">Minha conta</Link>
              <Link to="/carrinho">Meu carrinho</Link>
            </div>
          </nav>
        </div>

        <p className="site-footer__legal">© {CURRENT_YEAR} Marketplace · Projeto de estudo, sem vendas reais.</p>
      </div>
    </footer>
  )
}
