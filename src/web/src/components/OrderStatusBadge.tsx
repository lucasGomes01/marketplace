import { orderStatusLabel, type OrderStatus } from '../api/orders'

export function OrderStatusBadge({ status }: { status: OrderStatus }) {
  return <span className={`badge badge--order-${status.toLowerCase()}`}>{orderStatusLabel[status]}</span>
}
