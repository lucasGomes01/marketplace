import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { ApiError } from '../api/client'
import { productsApi, stockLabel, type Product } from '../api/products'
import { formatPrice } from '../utils/money'

type Load = { status: 'loading' } | { status: 'not-found' } | { status: 'error' } | { status: 'ready'; product: Product }

export function ProductPage() {
  const { id = '' } = useParams()
  const location = useLocation()
  const justPublished = (location.state as { published?: boolean } | null)?.published === true
  // Guarda de qual produto é o resultado: se não for o atual, está carregando.
  const [result, setResult] = useState<{ id: string; load: Load } | null>(null)
  const load: Load = result?.id === id ? result.load : { status: 'loading' }

  useEffect(() => {
    const controller = new AbortController()
    productsApi
      .get(id, controller.signal)
      .then((product) => setResult({ id, load: { status: 'ready', product } }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        const notFound = error instanceof ApiError && error.status === 404
        setResult({ id, load: notFound ? { status: 'not-found' } : { status: 'error' } })
      })
    return () => controller.abort()
  }, [id])

  if (load.status === 'loading') {
    return (
      <p className="muted" role="status">
        Carregando…
      </p>
    )
  }
  if (load.status === 'not-found' || load.status === 'error') {
    return (
      <section className="panel">
        <h1>{load.status === 'not-found' ? 'Produto não encontrado' : 'Não foi possível carregar o produto'}</h1>
        <p className="muted">
          {load.status === 'not-found' ? 'Ele pode ter sido removido ou o endereço está errado.' : 'Tente novamente em instantes.'}
        </p>
        <Link to="/">Ver todos os produtos</Link>
      </section>
    )
  }

  const { product } = load
  return (
    <>
      <p className="breadcrumb">
        <Link to="/">← Produtos</Link>
      </p>
      {justPublished ? (
        <div className="alert alert--success product-alert" role="status">
          Produto publicado! Ele já aparece na vitrine.
        </div>
      ) : null}
      <article className="product-detail">
        <div className="product-detail__thumb" aria-hidden="true">
          {product.name.charAt(0).toUpperCase()}
        </div>
        <div className="product-detail__info">
          <h1>{product.name}</h1>
          <p className="product-detail__price">{formatPrice(product.price, product.currency)}</p>
          <p className={`product-card__stock${product.stock <= 3 ? ' product-card__stock--low' : ''}`}>
            {stockLabel(product.stock)}
          </p>
          {product.description ? <p className="product-detail__description">{product.description}</p> : null}
          <button type="button" className="button button--primary" disabled title="O carrinho chega na próxima etapa">
            Carrinho em breve
          </button>
        </div>
      </article>
    </>
  )
}
