/** Tokens e ids da API falsa (módulo próprio para evitar import circular entre os handlers). */
export const validToken = 'valid-token'
export const buyerToken = 'buyer-token'

export const sellerUserId = '01a0edf9-3a0c-7243-8a4c-8db1e27c1be1'
export const buyerUserId = '01a0edf9-3a0c-7243-8a4c-8db1e27c1be2'

/** O token tem o papel Seller? (a conta "Maria" e quem ativou a venda). */
export function isSellerAuthorization(header: string | null): boolean {
  const token = header?.replace('Bearer ', '') ?? ''
  return token === validToken || token.startsWith('seller-token-')
}

/** Id do usuário dono do token (inclui o token novo emitido ao "Começar a vender"). */
export function userIdFromAuthorization(header: string | null): string | null {
  const token = header?.replace('Bearer ', '') ?? ''
  if (token === validToken) return sellerUserId
  if (token === buyerToken) return buyerUserId
  if (token.startsWith('seller-token-')) return token.slice('seller-token-'.length)
  return null
}
