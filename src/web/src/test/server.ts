import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { Role } from '../api/users'
import { cartHandlers } from './cart'
import { productHandlers } from './products'
import { buyerToken, buyerUserId, sellerUserId, validToken } from './tokens'

export { buyerToken, validToken }

interface FakeUser {
  id: string
  email: string
  name: string
  password: string
  roles: Role[]
  sellerSince: string | null
}

/** Conta que compra e vende. */
export const existingUser: FakeUser = {
  id: sellerUserId,
  email: 'maria@example.com',
  name: 'Maria Silva',
  password: 'Sup3r-secret',
  roles: ['Buyer', 'Seller'],
  sellerSince: '2026-10-01T12:00:00Z',
}

/** Conta que só compra (pode ativar o perfil de vendedor). */
export const buyerUser: FakeUser = {
  id: buyerUserId,
  email: 'joao@example.com',
  name: 'João Souza',
  password: 'Sup3r-secret',
  roles: ['Buyer'],
  sellerSince: null,
}


const inOneHour = () => new Date(Date.now() + 60 * 60 * 1000).toISOString()

/** Estado da API falsa; recriado a cada teste em `resetFakeApi`. */
let tokens = new Map<string, FakeUser>()

export function resetFakeApi() {
  tokens = new Map([
    [validToken, { ...existingUser }],
    [buyerToken, { ...buyerUser, roles: [...buyerUser.roles] }],
  ])
}
resetFakeApi()

const publicUser = ({ password: _password, ...user }: FakeUser) => user

const userFrom = (request: Request) => tokens.get(request.headers.get('Authorization')?.replace('Bearer ', '') ?? '')

/**
 * Handlers padrão: imitam o contrato real da API (via gateway).
 * Cada teste pode sobrescrever com `server.use(...)`.
 */
export const handlers = [
  http.post('/api/users', async ({ request }) => {
    const body = (await request.json()) as Record<string, string>
    if (body.email?.trim().toLowerCase() === existingUser.email) {
      return HttpResponse.json(
        { status: 409, title: 'Email already registered.', code: 'email.already_registered' },
        { status: 409 },
      )
    }
    return HttpResponse.json(
      { id: crypto.randomUUID(), email: body.email.trim().toLowerCase(), name: body.name.trim(), roles: ['Buyer'], sellerSince: null },
      { status: 201 },
    )
  }),

  http.post('/api/auth/login', async ({ request }) => {
    const body = (await request.json()) as Record<string, string>
    const entry = [...tokens.entries()].find(
      ([, user]) => user.email === body.email?.trim().toLowerCase() && user.password === body.password,
    )
    if (!entry) {
      return HttpResponse.json({ status: 401, title: 'Invalid email or password.' }, { status: 401 })
    }
    return HttpResponse.json({ accessToken: entry[0], tokenType: 'Bearer', expiresAt: inOneHour() })
  }),

  http.get('/api/users/me', ({ request }) => {
    const user = userFrom(request)
    return user ? HttpResponse.json(publicUser(user)) : new HttpResponse(null, { status: 401 })
  }),

  http.post('/api/users/me/seller', ({ request }) => {
    const user = userFrom(request)
    if (!user) {
      return new HttpResponse(null, { status: 401 })
    }
    const seller: FakeUser = {
      ...user,
      roles: ['Buyer', 'Seller'],
      sellerSince: user.sellerSince ?? new Date().toISOString(),
    }
    const newToken = `seller-token-${user.id}`
    tokens.set(newToken, seller)
    return HttpResponse.json({ user: publicUser(seller), accessToken: newToken, tokenType: 'Bearer', expiresAt: inOneHour() })
  }),
]

export const server = setupServer(...handlers, ...productHandlers, ...cartHandlers)
