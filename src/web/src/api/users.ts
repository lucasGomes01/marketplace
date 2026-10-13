import { apiFetch } from './client'

export type Role = 'Buyer' | 'Seller'

export interface User {
  id: string
  email: string
  name: string
  role: Role
}

export interface RegisterInput {
  name: string
  email: string
  password: string
  role: Role
}

export interface AccessToken {
  accessToken: string
  tokenType: 'Bearer'
  expiresAt: string
}

export const roleLabels: Record<Role, string> = {
  Buyer: 'Comprador',
  Seller: 'Vendedor',
}

export const usersApi = {
  register: (input: RegisterInput) => apiFetch<User>('/api/users', { method: 'POST', body: input }),

  login: (email: string, password: string) =>
    apiFetch<AccessToken>('/api/auth/login', { method: 'POST', body: { email, password } }),

  me: (token: string, signal?: AbortSignal) => apiFetch<User>('/api/users/me', { token, signal }),
}
