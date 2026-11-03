import { payoutStatusLabel, type PayoutStatus } from '../api/payments'

export function PayoutStatusBadge({ status }: { status: PayoutStatus }) {
  return <span className={`badge badge--payout-${status.toLowerCase()}`}>{payoutStatusLabel[status]}</span>
}
