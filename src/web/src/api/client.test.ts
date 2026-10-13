import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { ApiError, apiFetch } from './client'

describe('apiFetch', () => {
  it('returns the parsed JSON body on success', async () => {
    server.use(http.get('/api/ping', () => HttpResponse.json({ pong: true })))

    await expect(apiFetch<{ pong: boolean }>('/api/ping')).resolves.toEqual({ pong: true })
  })

  it('sends JSON body and bearer token', async () => {
    let received: { auth: string | null; contentType: string | null; body: unknown } | undefined
    server.use(
      http.post('/api/echo', async ({ request }) => {
        received = {
          auth: request.headers.get('Authorization'),
          contentType: request.headers.get('Content-Type'),
          body: await request.json(),
        }
        return HttpResponse.json({})
      }),
    )

    await apiFetch('/api/echo', { method: 'POST', body: { a: 1 }, token: 'abc' })

    expect(received).toEqual({ auth: 'Bearer abc', contentType: 'application/json', body: { a: 1 } })
  })

  it('turns a validation problem into field errors', async () => {
    server.use(
      http.post('/api/users', () =>
        HttpResponse.json(
          {
            status: 400,
            title: 'One or more validation errors occurred.',
            errors: { password: ['Password must have at least 8 characters.'], email: ['Invalid email.'] },
          },
          { status: 400 },
        ),
      ),
    )

    const error = await apiFetch('/api/users', { method: 'POST', body: {} }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).fieldErrors).toEqual({
      password: 'Password must have at least 8 characters.',
      email: 'Invalid email.',
    })
  })

  it('handles errors without body', async () => {
    server.use(http.get('/api/broken', () => new HttpResponse(null, { status: 500 })))

    const error = (await apiFetch('/api/broken').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(500)
    expect(error.title).toBe('Erro inesperado. Tente novamente.')
    expect(error.fieldErrors).toEqual({})
  })

  it('returns undefined for 204 No Content', async () => {
    server.use(http.delete('/api/thing', () => new HttpResponse(null, { status: 204 })))

    await expect(apiFetch('/api/thing', { method: 'DELETE' })).resolves.toBeUndefined()
  })
})
