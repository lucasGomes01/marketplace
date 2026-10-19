import { shipmentStatusLabel, type ShipmentStatus } from '../api/orders'

export function ShipmentStatusBadge({ status }: { status: ShipmentStatus }) {
  return <span className={`badge badge--shipment-${status.toLowerCase()}`}>{shipmentStatusLabel[status]}</span>
}
