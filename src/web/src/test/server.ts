import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

/** Usuário "cadastrado" na API falsa. */
export const existingUser = {
  id: '01a0edf9-3a0c-7243-8a4c-8db1e27c1be1',
  email: 'maria@example.com',
  name: 'Maria Silva',
  role: 'Seller' as const,
  password: 'Sup3r-secret',
}

export const validToken = 'valid-token'

const invalidCredentials = () =>
  HttpResponse.json({ status: 401, title: 'Invalid email or password.' }, { status: 401 })

/**
 * Handlers padrão: imitam o contrato real da API (via gateway).
 * Cada teste pode sobrescrever com `server.use(...)`.
 */
export const handlers = [
  http.post('/api/users', async ({ request }) => {
    const body = (await request.json()) as Record<string, string>
    if (body.email?.trim().toLowerCase() === existingUser.email) {
      return HttpResponse.json({ status: 409, title: 'Email already registered.' }, { status: 409 })
    }
    return HttpResponse.json(
      { id: crypto.randomUUID(), email: body.email.trim().toLowerCase(), name: body.name.trim(), role: body.role },
      { status: 201 },
    )
  }),

  http.post('/api/auth/login', async ({ request }) => {
    const body = (await request.json()) as Record<string, string>
    if (body.email?.trim().toLowerCase() !== existingUser.email || body.password !== existingUser.password) {
      return invalidCredentials()
    }
    return HttpResponse.json({
      accessToken: validToken,
      tokenType: 'Bearer',
      expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    })
  }),

  http.get('/api/users/me', ({ request }) => {
    if (request.headers.get('Authorization') !== `Bearer ${validToken}`) {
      return new HttpResponse(null, { status: 401 })
    }
    const { password: _password, ...user } = existingUser
    return HttpResponse.json(user)
  }),
]

export const server = setupServer(...handlers)
