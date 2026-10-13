import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { productsApi, type Page, type Product } from '../api/products'
import { useAuth } from '../auth/context'
import { ProductCard } from '../components/ProductCard'

export const PAGE_SIZE = 12

type Load = { status: 'loading' } | { status: 'error' } | { status: 'ready'; page: Page<Product> }

export function HomePage() {
  const { state } = useAuth()
  const [searchParams] = useSearchParams()
  const page = Math.max(1, Number(searchParams.get('pagina')) || 1)
  // Guarda de qual página é o resultado: se não for a atual, está carregando (sem setState síncrono no efeito).
  const [result, setResult] = useState<{ page: number; load: Load } | null>(null)
  const load: Load = result?.page === page ? result.load : { status: 'loading' }

  useEffect(() => {
    const controller = new AbortController()
    productsApi
      .list(page, PAGE_SIZE, controller.signal)
      .then((data) => setResult({ page, load: { status: 'ready', page: data } }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setResult({ page, load: { status: 'error' } })
        }
      })
    return () => controller.abort()
  }, [page])

  return (
    <>
      <section className="hero hero--compact">
        <h1>Compre e venda direto de quem faz.</h1>
        <p className="hero__lead">
          {state.status === 'authenticated'
            ? `Olá, ${state.user.name.split(' ')[0]}! Veja o que chegou.`
            : 'Vários vendedores, um carrinho só.'}
        </p>
      </section>

      <section aria-labelledby="catalog-title" className="catalog">
        <h2 id="catalog-title" className="catalog__title">
          Produtos
        </h2>
        <Catalog load={load} page={page} />
      </section>
    </>
  )
}

function Catalog({ load, page }: { load: Load; page: number }) {
  if (load.status === 'loading') {
    return (
      <p className="muted" role="status">
        Carregando produtos…
      </p>
    )
  }
  if (load.status === 'error') {
    return (
      <div className="alert alert--error" role="alert">
        Não foi possível carregar os produtos. Tente recarregar a página.
      </div>
    )
  }

  const { items, totalCount } = load.page
  if (totalCount === 0) {
    return (
      <div className="empty-state">
        <p>Nenhum produto à venda ainda.</p>
        <Link to="/vender/novo" className="button button--primary">
          Anunciar um produto
        </Link>
      </div>
    )
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  return (
    <>
      <div className="product-grid">
        {items.map((product) => (
          <ProductCard key={product.id} product={product} />
        ))}
      </div>
      {totalPages > 1 ? (
        <nav className="pagination" aria-label="Paginação">
          {page > 1 ? (
            <Link to={page === 2 ? '/' : `/?pagina=${page - 1}`} className="button button--ghost">
              Anterior
            </Link>
          ) : (
            <span />
          )}
          <span className="muted">
            Página {page} de {totalPages}
          </span>
          {page < totalPages ? (
            <Link to={`/?pagina=${page + 1}`} className="button button--ghost">
              Próxima
            </Link>
          ) : (
            <span />
          )}
        </nav>
      ) : null}
    </>
  )
}
