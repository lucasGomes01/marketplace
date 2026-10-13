import { apiFetch } from './client'

export type Role = 'Buyer' | 'Seller'

export interface User {
  id: string
  email: string
  name: string
  /** Toda conta tem 'Buyer'; 'Seller' aparece depois de "Começar a vender". */
  roles: Role[]
  sellerSince: string | null
}

export interface RegisterInput {
  name: string
  email: string
  password: string
}

export interface AccessToken {
  accessToken: string
  tokenType: 'Bearer'
  expiresAt: string
}

export interface BecomeSellerResult extends AccessToken {
  user: User
}

export const isSeller = (user: User) => user.roles.includes('Seller')

export const accountTypeLabel = (user: User) => (isSeller(user) ? 'Comprador e vendedor' : 'Comprador')

export const usersApi = {
  register: (input: RegisterInput) => apiFetch<User>('/api/users', { method: 'POST', body: input }),

  login: (email: string, password: string) =>
    apiFetch<AccessToken>('/api/auth/login', { method: 'POST', body: { email, password } }),

  me: (token: string, signal?: AbortSignal) => apiFetch<User>('/api/users/me', { token, signal }),

  becomeSeller: (token: string) => apiFetch<BecomeSellerResult>('/api/users/me/seller', { method: 'POST', token }),
}
