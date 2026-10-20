import { Link } from 'react-router'
import { discountPercent, stockLabel, type Product } from '../api/products'
import { conditionLabel } from '../catalog/categories'
import { formatPrice } from '../utils/money'
import { ProductImage } from './ProductImage'

interface ProductCardProps {
  product: Product
  /** Produtos relacionados: só foto, nome e preço. */
  compact?: boolean
  /** Pré-visualização do formulário de anúncio: sem link e fora da lista de produtos (não é um `article`). */
  preview?: boolean
}

/** Cartão da vitrine. */
export function ProductCard({ product, compact = false, preview = false }: ProductCardProps) {
  const soldOut = product.stock <= 0
  const discount = discountPercent(product)
  const condition = product.condition && product.condition !== 'New' ? conditionLabel(product.condition) : null
  const classes = ['product-card', soldOut && 'product-card--sold-out', compact && 'product-card--compact', preview && 'product-card--preview']
    .filter(Boolean)
    .join(' ')

  const content = (
    <>
      <ProductImage src={product.imageUrls?.[0]} className="product-card__thumb" />
      <div className="product-card__body">
        {product.brand || condition ? (
          <p className="product-card__meta">
            {product.brand ? <span>{product.brand}</span> : null}
            {condition ? <span>{condition}</span> : null}
          </p>
        ) : null}
        <h3 className="product-card__name">{product.name}</h3>
        {discount && product.originalPrice ? (
          <p className="product-card__original">
            <span className="sr-only">De </span>
            <s>{formatPrice(product.originalPrice, product.currency)}</s>
          </p>
        ) : null}
        <p className="product-card__price">
          {formatPrice(product.price, product.currency)}
          {discount ? <span className="product-card__discount">{discount}% OFF</span> : null}
        </p>
        {!compact && product.freeShipping ? <p className="product-card__shipping">Frete grátis</p> : null}
        {!compact ? (
          <p className={`product-card__stock${product.stock <= 3 ? ' product-card__stock--low' : ''}`}>{stockLabel(product.stock)}</p>
        ) : null}
      </div>
    </>
  )

  if (preview) {
    return (
      <div className={classes}>
        <div className="product-card__link">{content}</div>
      </div>
    )
  }
  return (
    <article className={classes}>
      <Link to={`/produtos/${product.id}`} className="product-card__link">
        {content}
      </Link>
    </article>
  )
}
