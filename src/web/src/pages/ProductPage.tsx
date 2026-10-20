import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { ApiError } from '../api/client'
import { discountPercent, productsApi, stockLabel, type Product } from '../api/products'
import { categoryLabel, conditionLabel } from '../catalog/categories'
import { AddToCartButton } from '../components/AddToCartButton'
import { Icon } from '../components/Icon'
import { ProductCard } from '../components/ProductCard'
import { ProductImage } from '../components/ProductImage'
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
  const category = categoryLabel(product.category)
  const discount = discountPercent(product)
  const specs = specifications(product)

  return (
    <>
      <nav className="breadcrumb" aria-label="Você está em">
        <Link to="/">Início</Link>
        {category ? (
          <>
            <Icon name="chevronRight" size={14} />
            <Link to={`/?categoria=${product.category}`}>{category}</Link>
          </>
        ) : null}
        <Icon name="chevronRight" size={14} />
        <span aria-current="page" className="breadcrumb__current">
          {product.name}
        </span>
      </nav>
      {justPublished ? (
        <div className="alert alert--success product-alert" role="status">
          Produto publicado! Ele já aparece na vitrine.
        </div>
      ) : null}

      <article className="product-detail">
        <Gallery key={product.id} product={product} />

        <div className="product-detail__info">
          <div className="product-detail__tags">
            <span className="chip">{conditionLabel(product.condition)}</span>
            {product.brand ? <span className="chip chip--muted">{product.brand}</span> : null}
            {product.sku ? <span className="product-detail__sku muted">Cód. {product.sku}</span> : null}
          </div>
          <h1>{product.name}</h1>

          <div className="price-box">
            {discount && product.originalPrice ? (
              <p className="price-box__original">
                <span className="sr-only">De </span>
                <s>{formatPrice(product.originalPrice, product.currency)}</s>
                <span className="price-box__discount">{discount}% OFF</span>
              </p>
            ) : null}
            <p className="product-detail__price">{formatPrice(product.price, product.currency)}</p>
          </div>

          <ul className="perks">
            {product.freeShipping ? (
              <li className="perks__item perks__item--success">
                <Icon name="truck" size={18} /> <strong>Frete grátis</strong>
              </li>
            ) : (
              <li className="perks__item">
                <Icon name="truck" size={18} /> Enviado pelo vendedor, com cada etapa acompanhada
              </li>
            )}
            <li className="perks__item">
              <Icon name="shield" size={18} />
              {product.warrantyMonths
                ? `Garantia de ${product.warrantyMonths} ${product.warrantyMonths === 1 ? 'mês' : 'meses'} do vendedor`
                : 'Estoque reservado ao confirmar o pedido'}
            </li>
            <li className="perks__item">
              <Icon name="package" size={18} />
              <span className={`product-card__stock${product.stock <= 3 ? ' product-card__stock--low' : ''}`}>
                {stockLabel(product.stock)}
              </span>
            </li>
          </ul>

          <AddToCartButton product={product} />

          <div className="seller-box">
            <Icon name="store" size={22} />
            <p>
              <strong>Vendido e enviado por um vendedor parceiro</strong>
              <span className="muted">Acompanhe cada etapa da entrega em “Pedidos”.</span>
            </p>
          </div>
        </div>
      </article>

      <div className="product-sections">
        <section className="panel product-section" aria-labelledby="description-title">
          <h2 id="description-title">Descrição</h2>
          {product.description ? (
            <p className="product-detail__description">{product.description}</p>
          ) : (
            <p className="muted">O vendedor não incluiu uma descrição.</p>
          )}
        </section>

        {specs.length > 0 ? (
          <section className="panel product-section" aria-labelledby="specs-title">
            <h2 id="specs-title">Ficha técnica</h2>
            <table className="specs">
              <tbody>
                {specs.map(([name, value]) => (
                  <tr key={name}>
                    <th scope="row">{name}</th>
                    <td>{value}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        ) : null}
      </div>

      <RelatedProducts product={product} />
    </>
  )
}

function Gallery({ product }: { product: Product }) {
  const images = product.imageUrls ?? []
  const [selected, setSelected] = useState(0)

  return (
    <div className="gallery">
      <ProductImage src={images[selected]} name={product.name} alt={product.name} className="gallery__main" />
      {images.length > 1 ? (
        <ul className="gallery__thumbs" aria-label="Fotos do produto">
          {images.map((url, index) => (
            <li key={url}>
              <button
                type="button"
                className={`gallery__thumb${index === selected ? ' gallery__thumb--active' : ''}`}
                aria-label={`Ver foto ${index + 1} de ${images.length}`}
                aria-pressed={index === selected}
                onClick={() => setSelected(index)}
              >
                <ProductImage src={url} name={product.name} />
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  )
}

/** Linhas da ficha técnica: o que o vendedor informou mais os dados do anúncio. */
function specifications(product: Product): [string, string][] {
  const rows: [string, string][] = []
  if (product.brand) rows.push(['Marca', product.brand])
  if (product.condition) rows.push(['Condição', conditionLabel(product.condition)])
  for (const attribute of product.attributes ?? []) {
    rows.push([attribute.name, attribute.value])
  }
  const { lengthCm, widthCm, heightCm } = product
  if (lengthCm && widthCm && heightCm) rows.push(['Dimensões da embalagem', `${lengthCm} × ${widthCm} × ${heightCm} cm`])
  if (product.weightGrams) {
    rows.push(['Peso', product.weightGrams >= 1000 ? `${(product.weightGrams / 1000).toLocaleString('pt-BR')} kg` : `${product.weightGrams} g`])
  }
  if (product.warrantyMonths) rows.push(['Garantia', `${product.warrantyMonths} ${product.warrantyMonths === 1 ? 'mês' : 'meses'}`])
  if (product.sku) rows.push(['Código (SKU)', product.sku])
  // A mesma linha não aparece duas vezes (ex.: o vendedor também escreveu "Marca" na ficha).
  return rows.filter(([name], index) => rows.findIndex(([other]) => other.toLowerCase() === name.toLowerCase()) === index)
}

function RelatedProducts({ product }: { product: Product }) {
  const [related, setRelated] = useState<{ id: string; items: Product[] } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    productsApi
      .list(1, 5, controller.signal, product.category ? { category: product.category } : {})
      .then((page) => setRelated({ id: product.id, items: page.items.filter((p) => p.id !== product.id).slice(0, 4) }))
      .catch(() => {
        // Vitrine extra: se falhar, a página do produto continua completa.
      })
    return () => controller.abort()
  }, [product.id, product.category])

  const items = related?.id === product.id ? related.items : []
  if (items.length === 0) {
    return null
  }
  return (
    <section className="related" aria-labelledby="related-title">
      <h2 id="related-title" className="section-title">
        Você também pode gostar
      </h2>
      <div className="product-grid product-grid--related">
        {items.map((item) => (
          <ProductCard key={item.id} product={item} compact />
        ))}
      </div>
    </section>
  )
}
