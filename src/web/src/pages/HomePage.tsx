import { Link } from 'react-router'
import { useAuth } from '../auth/context'

export function HomePage() {
  const { state } = useAuth()

  return (
    <section className="hero">
      <h1>{state.status === 'authenticated' ? `Olá, ${state.user.name.split(' ')[0]}!` : 'Compre e venda direto de quem faz.'}</h1>
      <p className="hero__lead">
        Um marketplace com vários vendedores: cada um cuida dos seus produtos, você monta um carrinho só.
      </p>
      {state.status === 'anonymous' ? (
        <Link to="/cadastro" className="button button--primary">
          Começar agora
        </Link>
      ) : null}
      <p className="hero__soon muted">Em breve por aqui: catálogo de produtos, carrinho e pedidos.</p>
    </section>
  )
}
