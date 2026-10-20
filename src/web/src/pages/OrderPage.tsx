import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { ApiError } from '../api/client'
import { orderPolling, ordersApi, type Order } from '../api/orders'
import { useAuth } from '../auth/context'
import { OrderStatusBadge } from '../components/OrderStatusBadge'
import { ShipmentStatusBadge } from '../components/ShipmentStatusBadge'
import { ShipmentTimeline } from '../components/ShipmentTimeline'
import { formatDateTime, shortOrderId as shortId } from '../utils/dates'
import { formatPrice } from '../utils/money'

/**
 * waiting = o pedido ainda não existe na API (o checkout é assíncrono); polling = ainda vamos consultar de novo.
 */
type Load =
  | { status: 'waiting'; polling: boolean }
  | { status: 'not-found' }
  | { status: 'error' }
  | { status: 'ready'; order: Order; polling: boolean }

export function OrderPage() {
  const { id = '' } = useParams()
  const location = useLocation()
  const placed = (location.state as { placed?: boolean } | null)?.placed === true
  const { state } = useAuth()
  const token = state.status === 'authenticated' ? state.token : ''

  // Guarda de qual pedido/sessão é o resultado: se não for o atual, está carregando.
  const key = `${id}:${token}`
  const [result, setResult] = useState<{ key: string; load: Load } | null>(null)
  const load: Load = result?.key === key ? result.load : { status: 'waiting', polling: true }

  useEffect(() => {
    if (!token) {
      return
    }
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined

    const poll = async (attempt: number) => {
      const again = attempt + 1 < orderPolling.maxAttempts
      const retry = () => {
        timer = setTimeout(() => void poll(attempt + 1), orderPolling.intervalMs)
      }
      try {
        const order = await ordersApi.get(id, token, controller.signal)
        const polling = order.status === 'Pending' && again
        setResult({ key, load: { status: 'ready', order, polling } })
        if (polling) retry()
      } catch (error) {
        if (controller.signal.aborted) {
          return
        }
        const notFound = error instanceof ApiError && error.status === 404
        if (notFound && placed) {
          // Acabou de finalizar: o Orders ainda não criou o pedido.
          setResult({ key, load: { status: 'waiting', polling: again } })
          if (again) retry()
          return
        }
        setResult({ key, load: notFound ? { status: 'not-found' } : { status: 'error' } })
      }
    }
    void poll(0)

    return () => {
      controller.abort()
      clearTimeout(timer)
    }
  }, [id, token, key, placed])

  if (load.status === 'not-found' || load.status === 'error') {
    return (
      <section className="panel">
        <h1>{load.status === 'not-found' ? 'Pedido não encontrado' : 'Não foi possível carregar o pedido'}</h1>
        <p className="muted">
          {load.status === 'not-found' ? 'Confira o endereço ou veja a sua lista de pedidos.' : 'Tente novamente em instantes.'}
        </p>
        <Link to="/pedidos">Ver meus pedidos</Link>
      </section>
    )
  }

  if (load.status === 'waiting' && !placed) {
    return (
      <p className="muted" role="status">
        Carregando…
      </p>
    )
  }

  const order = load.status === 'ready' ? load.order : null
  const polling = load.polling
  return (
    <section className="order">
      <p className="breadcrumb">
        <Link to="/pedidos">← Meus pedidos</Link>
      </p>

      <div className="order__header">
        <h1>{placed ? 'Pedido recebido' : `Pedido ${shortId(id)}`}</h1>
        <OrderStatusBadge status={order?.status ?? 'Pending'} />
      </div>

      <OrderMessage order={order} polling={polling} />

      {order ? (
        <div className="order__layout">
          <ul className="order__items">
            {order.items.map((item) => (
              <li key={item.productId} className="order-item" aria-label={item.productName}>
                <Link to={`/produtos/${item.productId}`} className="order-item__name">
                  {item.productName}
                </Link>
                <span className="muted">
                  {item.quantity} × {formatPrice(item.unitPrice, item.currency)}
                </span>
                <strong className="order-item__subtotal">{formatPrice(item.subtotal, item.currency)}</strong>
              </li>
            ))}
          </ul>
          <aside className="order__summary panel" aria-label="Resumo do pedido">
            <p className="muted">Feito em {formatDateTime(order.createdAt)}</p>
            <p className="cart__total">
              Total <strong data-testid="order-total">{formatPrice(order.total, order.currency)}</strong>
            </p>
          </aside>
        </div>
      ) : null}

      {order?.status === 'Confirmed' && order.shipments.length > 0 ? (
        <Delivery
          order={order}
          token={token}
          onChange={(updated) => setResult({ key, load: { status: 'ready', order: updated, polling: false } })}
        />
      ) : null}
    </section>
  )
}

/** Um bloco por vendedor: itens, linha do tempo e "Recebi" quando o envio já saiu. */
function Delivery({ order, token, onChange }: { order: Order; token: string; onChange: (order: Order) => void }) {
  const [confirming, setConfirming] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const total = order.shipments.length

  async function confirm(sellerId: string) {
    setConfirming(sellerId)
    setError(null)
    try {
      onChange(await ordersApi.confirmDelivery(order.id, sellerId, token))
    } catch {
      setError('Não foi possível confirmar o recebimento. Tente novamente.')
    } finally {
      setConfirming(null)
    }
  }

  return (
    <section className="delivery" aria-labelledby="delivery-title">
      <h2 id="delivery-title">Entrega</h2>
      {error ? (
        <div className="alert alert--error" role="alert">
          {error}
        </div>
      ) : null}
      <div className="delivery__list">
        {order.shipments.map((shipment, index) => {
          const title = `Envio ${index + 1} de ${total}`
          const items = order.items.filter((item) => item.sellerId === shipment.sellerId)
          return (
            <article key={shipment.sellerId} className="shipment panel" aria-label={title}>
              <header className="shipment__header">
                <h3>{title}</h3>
                <ShipmentStatusBadge status={shipment.status} />
              </header>
              <p className="muted shipment__items">
                {items.map((item) => `${item.quantity} × ${item.productName}`).join(' · ')}
              </p>
              <ShipmentTimeline shipment={shipment} />
              {shipment.status === 'Shipped' ? (
                <button
                  type="button"
                  className="button button--primary"
                  disabled={confirming !== null}
                  onClick={() => void confirm(shipment.sellerId)}
                >
                  Recebi
                </button>
              ) : null}
            </article>
          )
        })}
      </div>
    </section>
  )
}

function OrderMessage({ order, polling }: { order: Order | null; polling: boolean }) {
  if (order?.status === 'Confirmed') {
    return (
      <div className="alert alert--success" role="status">
        Seu pedido foi confirmado e os produtos estão reservados para você.
      </div>
    )
  }
  if (order?.status === 'Rejected') {
    return (
      <div className="alert alert--error" role="alert">
        <strong>Não foi possível concluir o pedido.</strong> {order.rejectionReason ?? ''}
      </div>
    )
  }
  if (polling) {
    return (
      <p className="muted order__progress" role="status">
        Estamos confirmando o estoque com os vendedores…
      </p>
    )
  }
  return (
    <div className="alert" role="status">
      Seu pedido ainda está sendo processado. Volte em alguns instantes.{' '}
      <Link to="/pedidos">Ver meus pedidos</Link>
    </div>
  )
}
