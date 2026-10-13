import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { manyProducts, resetFakeProducts } from '../test/products'

const cards = () => screen.getAllByRole('article')

describe('HomePage (vitrine)', () => {
  it('lists products newest first with price and stock', async () => {
    renderApp('/')

    expect(await screen.findByRole('heading', { name: 'Trena a laser 40m' })).toBeInTheDocument()
    expect(cards().map((card) => within(card).getByRole('heading').textContent)).toEqual([
      'Trena a laser 40m',
      'Jogo de chaves combinadas',
      'Furadeira de impacto 700W',
    ])

    const furadeira = cards()[2]
    expect(within(furadeira).getByText(/R\$\s199,90/)).toBeInTheDocument()
    expect(within(furadeira).getByText('5 em estoque')).toBeInTheDocument()
    expect(within(cards()[1]).getByText('Últimas 2 unidades')).toBeInTheDocument()
    expect(within(cards()[0]).getByText('Esgotado')).toBeInTheDocument()
  })

  it('links each card to the product page', async () => {
    renderApp('/')

    const link = await screen.findByRole('link', { name: /Furadeira de impacto 700W/ })

    expect(link).toHaveAttribute('href', '/produtos/p1-0000-0000-0000-000000000001')
  })

  it('shows an empty state inviting people to sell', async () => {
    resetFakeProducts([])
    renderApp('/')

    expect(await screen.findByText('Nenhum produto à venda ainda.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Anunciar um produto' })).toHaveAttribute('href', '/vender/novo')
  })

  it('paginates 12 per page', async () => {
    resetFakeProducts(manyProducts(15))
    const { user } = renderApp('/')

    await screen.findByRole('heading', { name: 'Produto 1' })
    expect(cards()).toHaveLength(12)
    expect(screen.queryByRole('link', { name: 'Anterior' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('link', { name: 'Próxima' }))

    expect(await screen.findByRole('heading', { name: 'Produto 13' })).toBeInTheDocument()
    expect(cards()).toHaveLength(3)
    expect(screen.getByText('Página 2 de 2')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Próxima' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Anterior' })).toBeInTheDocument()
  })
})
