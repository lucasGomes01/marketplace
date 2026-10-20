/** 15/10/2026, 07:00 */
export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })

/** 15/10/2026 */
export const formatDate = (iso: string) => new Date(iso).toLocaleDateString('pt-BR')

/** #01A0EDF9 — como os pedidos aparecem para as pessoas. */
export const shortOrderId = (id: string) => `#${id.slice(0, 8).toUpperCase()}`
