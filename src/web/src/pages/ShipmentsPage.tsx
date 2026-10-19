import { useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import { nextSellerStep, shipmentsApi, shipmentStatusLabel, type SellerShipment, type ShipmentStatus } from '../api/orders'
import { isSeller } from '../api/users'
import { useAuth } from '../auth/context'
import { BecomeSellerCallout } from '../components/BecomeSellerCallout'
import { ShipmentStatusBadge } from '../components/ShipmentStatusBadge'
import { formatDate, shortOrderId } from '../utils/dates'
import { formatPrice } from '../utils/money'

/** "Pedidos a despachar": o vendedor leva cada envio dele de "aguardando separação" até "enviado". */
export function ShipmentsPage() {
  const { state } = useAuth()

  if (state.status !== 'authenticated') {
    return null
  }
  if (!isSeller(state.user)) {
    return (
      <section className="panel">
        <h1>Pedidos a despachar</h1>
        <BecomeSellerCallout
          title="Ative a venda na sua conta"
          description="Os pedidos dos seus produtos aparecem aqui para você separar, embalar e enviar."
        />
      </section>
    )
  }
  return <SellerShipments token={state.token} />
}

const isOut = (s: SellerShipment) => s.status === 'Shipped' || s.status === 'Delivered'

function SellerShipments({ token }: { token: string }) {
  // Guarda de qual sessão/recarga é o resultado; ao recarregar mantém a lista anterior na tela.
  const [reload, setReload] = useState(0)
  const [result, setResult] = useState<{ token: string; shipments: SellerShipment[] | 'error' } | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    shipmentsApi
      .list(token, controller.signal)
      .then((shipments) => setResult({ token, shipments }))
      .catch(() => {
        if (!controller.signal.aborted) setResult({ token, shipments: 'error' })
      })
    return () => controller.abort()
  }, [token, reload])

  async function advance(shipment: SellerShipment, to: ShipmentStatus) {
    setBusy(shipment.orderId)
    setNotice(null)
    try {
      const updated = await shipmentsApi.advance(shipment.orderId, to, token)
      setResult((current) =>
        current && current.shipments !== 'error'
          ? { ...current, shipments: current.shipments.map((s) => (s.orderId === updated.orderId ? updated : s)) }
          : current,
      )
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setNotice('Este envio foi alterado em outro lugar. Atualizamos a lista.')
        setReload((n) => n + 1)
      } else {
        setNotice('Não foi possível atualizar o envio. Tente novamente.')
      }
    } finally {
      setBusy(null)
    }
  }

  const shipments = result?.token === token ? result.shipments : null

  if (shipments === null) {
    return (
      <p className="muted" role="status">
        Carregando envios…
      </p>
    )
  }

  return (
    <section className="shipments">
      <h1>Pedidos a despachar</h1>
      {notice ? (
        <div className="alert alert--error" role="alert">
          {notice}
        </div>
      ) : null}

      {shipments === 'error' ? (
        <div className="alert alert--error" role="alert">
          Não foi possível carregar seus envios. Tente novamente em instantes.
        </div>
      ) : shipments.length === 0 ? (
        <div className="panel empty-state">
          <p>Nenhum pedido para despachar ainda.</p>
        </div>
      ) : (
        <>
          <ShipmentGroup
            title="A despachar"
            empty="Tudo despachado por aqui."
            shipments={shipments.filter((s) => !isOut(s))}
            busy={busy}
            onAdvance={advance}
          />
          <ShipmentGroup
            title="Enviados"
            empty="Nenhum envio saiu ainda."
            shipments={shipments.filter(isOut)}
            busy={busy}
            onAdvance={advance}
          />
        </>
      )}
    </section>
  )
}

interface GroupProps {
  title: string
  empty: string
  shipments: SellerShipment[]
  busy: string | null
  onAdvance: (shipment: SellerShipment, to: ShipmentStatus) => void
}

function ShipmentGroup({ title, empty, shipments, busy, onAdvance }: GroupProps) {
  const id = `shipments-${title.replace(/\s+/g, '-').toLowerCase()}`
  return (
    <section className="shipments__group" aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      {shipments.length === 0 ? (
        <p className="muted">{empty}</p>
      ) : (
        <div className="shipments__list">
          {shipments.map((shipment) => (
            <ShipmentCard
              key={shipment.orderId}
              shipment={shipment}
              busy={busy === shipment.orderId}
              disabled={busy !== null}
              onAdvance={onAdvance}
            />
          ))}
        </div>
      )}
    </section>
  )
}

interface CardProps {
  shipment: SellerShipment
  busy: boolean
  disabled: boolean
  onAdvance: (shipment: SellerShipment, to: ShipmentStatus) => void
}

function ShipmentCard({ shipment, busy, disabled, onAdvance }: CardProps) {
  const title = `Pedido ${shortOrderId(shipment.orderId)}`
  const next = nextSellerStep(shipment.status)
  return (
    <article className="shipment panel" aria-label={title}>
      <header className="shipment__header">
        <h3>{title}</h3>
        <ShipmentStatusBadge status={shipment.status} />
      </header>
      <p className="muted">
        Pedido em {formatDate(shipment.orderedAt)} · {shipment.itemCount === 1 ? '1 item' : `${shipment.itemCount} itens`}
      </p>
      <ul className="shipment__items">
        {shipment.items.map((item) => (
          <li key={item.productId}>
            {item.quantity} × {item.productName}
          </li>
        ))}
      </ul>
      <div className="shipment__footer">
        <strong>{formatPrice(shipment.total, shipment.currency)}</strong>
        {next ? (
          <button
            type="button"
            className="button button--primary"
            disabled={disabled}
            onClick={() => onAdvance(shipment, next)}
          >
            {busy ? 'Salvando…' : `Marcar como ${shipmentStatusLabel[next].toLowerCase()}`}
          </button>
        ) : shipment.status === 'Shipped' ? (
          <p className="muted">Aguardando o comprador confirmar o recebimento.</p>
        ) : shipment.deliveredAt ? (
          <p className="muted">Recebido pelo comprador em {formatDate(shipment.deliveredAt)}.</p>
        ) : null}
      </div>
    </article>
  )
}
