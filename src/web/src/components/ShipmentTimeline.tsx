import { reachedAt, shipmentStatusLabel, shipmentSteps, type Shipment } from '../api/orders'
import { formatDateTime } from '../utils/dates'

/** Separado → Embalado → Enviado → Entregue, com a data das etapas que já aconteceram. */
export function ShipmentTimeline({ shipment }: { shipment: Shipment }) {
  return (
    <ol className="timeline" aria-label="Andamento do envio">
      {shipmentSteps
        .filter((step) => step !== 'AwaitingPicking')
        .map((step) => {
          const at = reachedAt(shipment, step)
          return (
            <li key={step} className={at ? 'timeline__step timeline__step--done' : 'timeline__step'}>
              <span className="timeline__label">{shipmentStatusLabel[step]}</span>
              {at ? <time className="timeline__time muted" dateTime={at}> {formatDateTime(at)}</time> : null}
            </li>
          )
        })}
    </ol>
  )
}
