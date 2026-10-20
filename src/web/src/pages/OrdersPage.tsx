import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { deliverySummary, ordersApi, type Order } from '../api/orders'
import { useAuth } from '../auth/context'
import { OrderStatusBadge } from '../components/OrderStatusBadge'
import { formatPrice } from '../utils/money'

type Load = { status: 'loading' } | { status: 'error' } | { status: 'ready'; orders: Order[] }

export function OrdersPage() {
  const { state } = useAuth()
  const token = state.status === 'authenticated' ? state.token : ''
  // Guarda de qual sessão é o resultado: ao trocar de conta volta a carregar.
  const [result, setResult] = useState<{ token: string; load: Load } | null>(null)
  const load: Load = result?.token === token ? result.load : { status: 'loading' }

  useEffect(() => {
    if (!token) {
      return
    }
    const controller = new AbortController()
    ordersApi
      .list(token, controller.signal)
      .then((orders) => setResult({ token, load: { status: 'ready', orders } }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setResult({ token, load: { status: 'error' } })
        }
      })
    return () => controller.abort()
  }, [token])

  if (load.status === 'loading') {
    return (
      <p className="muted" role="status">
        Carregando pedidos…
      </p>
    )
  }
  if (load.status === 'error') {
    return (
      <section className="panel">
        <h1>Meus pedidos</h1>
        <div className="alert alert--error" role="alert">
          Não foi possível carregar seus pedidos. Tente novamente em instantes.
        </div>
      </section>
    )
  }
  if (load.orders.length === 0) {
    return (
      <section className="panel">
        <h1>Meus pedidos</h1>
        <div className="empty-state">
          <p>Você ainda não fez nenhum pedido.</p>
          <Link to="/" className="button button--primary">
            Ver produtos
          </Link>
        </div>
      </section>
    )
  }

  return (
    <section className="orders">
      <h1>Meus pedidos</h1>
      <ul className="orders__list">
        {load.orders.map((order) => (
          <li key={order.id} className="order-row">
            <Link to={`/pedidos/${order.id}`} className="order-row__link">
              <span className="order-row__main">
                <strong className="order-row__title">{order.items.map((i) => i.productName).join(', ')}</strong>
                <span className="muted">
                  <span>{new Date(order.createdAt).toLocaleDateString('pt-BR')}</span> ·{' '}
                  <span>{order.itemCount === 1 ? '1 item' : `${order.itemCount} itens`}</span>
                </span>
                {deliverySummary(order) ? <span className="muted">Entrega: {deliverySummary(order)}</span> : null}
              </span>
              <span className="order-row__side">
                <strong>{formatPrice(order.total, order.currency)}</strong>
                <OrderStatusBadge status={order.status} />
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
