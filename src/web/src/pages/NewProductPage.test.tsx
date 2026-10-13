import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { server } from '../test/server'

async function fillForm(user: ReturnType<typeof renderApp>['user'], { price = '1.234,56', stock = '7' } = {}) {
  await user.type(await screen.findByLabelText('Nome do produto'), 'Parafusadeira 12V')
  await user.type(screen.getByLabelText('Descrição'), 'Bateria de lítio')
  await user.type(screen.getByLabelText('Preço (R$)'), price)
  await user.clear(screen.getByLabelText('Estoque'))
  await user.type(screen.getByLabelText('Estoque'), stock)
}

describe('NewProductPage', () => {
  it('asks anonymous people to log in', async () => {
    renderApp('/vender/novo')

    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('invites a buyer-only account to start selling, then shows the form', async () => {
    const { user } = renderApp('/vender/novo', { loggedIn: 'buyer' })

    expect(await screen.findByRole('heading', { name: 'Ative a venda na sua conta' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Nome do produto')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Começar a vender' }))

    expect(await screen.findByLabelText('Nome do produto')).toBeInTheDocument()
  })

  it('publishes the product and shows its page', async () => {
    let sent: unknown
    server.use(
      http.post('/api/products', async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json(
          { id: 'new-1', sellerId: 's1', name: 'Parafusadeira 12V', description: 'Bateria de lítio', price: 1234.56, currency: 'BRL', stock: 7, createdAt: new Date().toISOString() },
          { status: 201 },
        )
      }),
      http.get('/api/products/new-1', () =>
        HttpResponse.json({ id: 'new-1', sellerId: 's1', name: 'Parafusadeira 12V', description: 'Bateria de lítio', price: 1234.56, currency: 'BRL', stock: 7, createdAt: new Date().toISOString() }),
      ),
    )
    const { user } = renderApp('/vender/novo', { loggedIn: true })

    await fillForm(user)
    await user.click(screen.getByRole('button', { name: 'Publicar produto' }))

    expect(await screen.findByRole('heading', { name: 'Parafusadeira 12V' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Produto publicado!')
    expect(sent).toEqual({ name: 'Parafusadeira 12V', description: 'Bateria de lítio', price: 1234.56, stock: 7 })
  })

  it('blocks a price that is not a number before calling the API', async () => {
    let called = false
    server.use(http.post('/api/products', () => {
      called = true
      return HttpResponse.json({}, { status: 201 })
    }))
    const { user } = renderApp('/vender/novo', { loggedIn: true })

    await fillForm(user, { price: 'abc' })
    await user.click(screen.getByRole('button', { name: 'Publicar produto' }))

    expect(await screen.findByText('Informe um preço válido, por exemplo 199,90.')).toBeInTheDocument()
    expect(called).toBe(false)
  })

  it('shows API validation errors in Portuguese below the fields', async () => {
    server.use(
      http.post('/api/products', () =>
        HttpResponse.json(
          {
            status: 400,
            errors: { price: ['Price must be greater than zero.'], stock: ['Stock cannot be negative.'] },
          },
          { status: 400 },
        ),
      ),
    )
    const { user } = renderApp('/vender/novo', { loggedIn: true })

    await fillForm(user, { price: '0' })
    await user.click(screen.getByRole('button', { name: 'Publicar produto' }))

    expect(await screen.findByText('O preço precisa ser maior que zero.')).toBeInTheDocument()
    expect(screen.getByText('O estoque não pode ser negativo.')).toBeInTheDocument()
    expect(screen.getByLabelText('Preço (R$)')).toHaveAttribute('aria-invalid', 'true')
  })

  it('is reachable from the header for logged in people', async () => {
    renderApp('/', { loggedIn: true })

    expect(await screen.findByRole('link', { name: 'Vender' })).toHaveAttribute('href', '/vender/novo')
  })
})
