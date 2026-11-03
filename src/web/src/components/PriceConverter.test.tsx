import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { sampleProducts } from '../test/products'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const wrench = sampleProducts[1] // R$ 89,50

describe('PriceConverter', () => {
  it('converts the price with the Mastercard rate when a currency is chosen', async () => {
    const { user } = renderApp(`/produtos/${wrench.id}`)
    const select = await screen.findByLabelText('Ver em outra moeda')

    await user.selectOptions(select, 'USD')

    expect(await screen.findByText(/≈\s*US\$\s16,41/)).toBeInTheDocument()
    expect(screen.getByText(/Cotação Mastercard de 03\/11\/2026\. A compra é em BRL\./)).toBeInTheDocument()
  })

  it('does not offer the product currency itself', async () => {
    renderApp(`/produtos/${wrench.id}`)
    await screen.findByLabelText('Ver em outra moeda')

    expect(screen.queryByRole('option', { name: 'BRL' })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'EUR' })).toBeInTheDocument()
  })

  it('does not ask for a rate until a currency is chosen', async () => {
    let calls = 0
    server.use(http.get('/api/exchange-rates/convert', () => (calls++, HttpResponse.json({}))))
    renderApp(`/produtos/${wrench.id}`)
    await screen.findByLabelText('Ver em outra moeda')

    expect(calls).toBe(0)
  })

  it('tells when the rate is unavailable', async () => {
    server.use(http.get('/api/exchange-rates/convert', () => new HttpResponse(null, { status: 503 })))
    const { user } = renderApp(`/produtos/${wrench.id}`)

    await user.selectOptions(await screen.findByLabelText('Ver em outra moeda'), 'EUR')

    expect(await screen.findByText('Não foi possível obter a cotação agora.')).toBeInTheDocument()
  })
})
