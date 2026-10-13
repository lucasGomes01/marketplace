import { Link } from 'react-router'
import { stockLabel, type Product } from '../api/products'
import { formatPrice } from '../utils/money'

export function ProductCard({ product }: { product: Product }) {
  const soldOut = product.stock <= 0

  return (
    <article className={`product-card${soldOut ? ' product-card--sold-out' : ''}`}>
      <Link to={`/produtos/${product.id}`} className="product-card__link">
        <div className="product-card__thumb" aria-hidden="true">
          {product.name.charAt(0).toUpperCase()}
        </div>
        <h3 className="product-card__name">{product.name}</h3>
        <p className="product-card__price">{formatPrice(product.price, product.currency)}</p>
        <p className={`product-card__stock${product.stock <= 3 ? ' product-card__stock--low' : ''}`}>
          {stockLabel(product.stock)}
        </p>
      </Link>
    </article>
  )
}
