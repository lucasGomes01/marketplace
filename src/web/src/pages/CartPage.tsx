import { useState } from 'react'
import { Link } from 'react-router'
import { cartErrorMessage } from '../api/cart'
import { useCart } from '../cart/context'
import { formatPrice } from '../utils/money'

export function CartPage() {
  const { cart, changeQuantity, remove } = useCart()
  const [busyProductId, setBusyProductId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  if (!cart) {
    return (
      <p className="muted" role="status">
        Carregando carrinho…
      </p>
    )
  }

  async function run(productId: string, operation: () => Promise<void>) {
    setBusyProductId(productId)
    setError(null)
    try {
      await operation()
    } catch (err) {
      setError(cartErrorMessage(err))
    } finally {
      setBusyProductId(null)
    }
  }

  if (cart.items.length === 0) {
    return (
      <section className="panel">
        <h1>Carrinho</h1>
        <div className="empty-state">
          <p>Seu carrinho está vazio.</p>
          <Link to="/" className="button button--primary">
            Ver produtos
          </Link>
        </div>
      </section>
    )
  }

  return (
    <section className="cart">
      <h1>Carrinho</h1>

      {error ? (
        <div className="alert alert--error" role="alert">
          {error}
        </div>
      ) : null}

      <div className="cart__layout">
        <ul className="cart__items">
          {cart.items.map((item) => {
            const busy = busyProductId === item.productId
            return (
              <li key={item.productId} className="cart-item" aria-label={item.productName}>
                <div className="cart-item__thumb" aria-hidden="true">
                  {item.productName.charAt(0).toUpperCase()}
                </div>
                <div className="cart-item__info">
                  <Link to={`/produtos/${item.productId}`} className="cart-item__name">
                    {item.productName}
                  </Link>
                  <span className="muted">{formatPrice(item.unitPrice, item.currency)} cada</span>
                </div>
                <div className="quantity" aria-label={`Quantidade de ${item.productName}`}>
                  <button
                    type="button"
                    className="quantity__button"
                    aria-label={`Diminuir quantidade de ${item.productName}`}
                    disabled={busy || item.quantity <= 1}
                    onClick={() => run(item.productId, () => changeQuantity(item.productId, item.quantity - 1))}
                  >
                    −
                  </button>
                  <output className="quantity__value" aria-live="polite">
                    {item.quantity}
                  </output>
                  <button
                    type="button"
                    className="quantity__button"
                    aria-label={`Aumentar quantidade de ${item.productName}`}
                    disabled={busy}
                    onClick={() => run(item.productId, () => changeQuantity(item.productId, item.quantity + 1))}
                  >
                    +
                  </button>
                </div>
                <strong className="cart-item__subtotal">{formatPrice(item.subtotal, item.currency)}</strong>
                <button
                  type="button"
                  className="link-button"
                  disabled={busy}
                  onClick={() => run(item.productId, () => remove(item.productId))}
                >
                  Remover
                </button>
              </li>
            )
          })}
        </ul>

        <aside className="cart__summary panel" aria-label="Resumo do pedido">
          <p className="cart__count">{cart.totalQuantity === 1 ? '1 item' : `${cart.totalQuantity} itens`}</p>
          <p className="cart__total">
            Total <strong data-testid="cart-total">{formatPrice(cart.total, cart.currency)}</strong>
          </p>
          <button type="button" className="button button--primary button--block" disabled>
            Finalizar pedido (em breve)
          </button>
          <Link to="/" className="cart__continue">
            Continuar comprando
          </Link>
        </aside>
      </div>
    </section>
  )
}
