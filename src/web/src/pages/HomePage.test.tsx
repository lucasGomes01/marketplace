import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { manyProducts, resetFakeProducts } from '../test/products'

const cards = () => screen.getAllByRole('article')

const waitForOrder = (names: string[]) =>
  waitFor(() => expect(cards().map((card) => within(card).getByRole('heading').textContent)).toEqual(names))

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

  it('shows brand, discount, condition and free shipping on the cards', async () => {
    renderApp('/')

    await screen.findByRole('heading', { name: 'Furadeira de impacto 700W' })
    const furadeira = cards()[2]
    expect(within(furadeira).getByText('Bosch')).toBeInTheDocument()
    expect(within(furadeira).getByText('20% OFF')).toBeInTheDocument()
    expect(within(furadeira).getByText('Frete grátis')).toBeInTheDocument()
    expect(within(cards()[1]).getByText('Usado')).toBeInTheDocument()
  })

  it('searches from the header and keeps the term in the address', async () => {
    const { user } = renderApp('/')
    await screen.findByRole('heading', { name: 'Trena a laser 40m' })

    await user.type(within(screen.getByRole('search')).getByRole('searchbox', { name: 'Buscar produtos' }), 'bosch')
    await user.click(within(screen.getByRole('search')).getByRole('button', { name: 'Buscar' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Resultados para “bosch”' })).toBeInTheDocument()
    expect(cards().map((card) => within(card).getByRole('heading').textContent)).toEqual(['Furadeira de impacto 700W'])
    expect(screen.getByText('1 produto')).toBeInTheDocument()
  })

  it('filters by category from the category bar and says when nothing matches', async () => {
    const { user } = renderApp('/')
    await screen.findByRole('heading', { name: 'Trena a laser 40m' })

    await user.click(within(screen.getByRole('navigation', { name: 'Categorias' })).getByRole('link', { name: 'Eletrônicos' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Eletrônicos' })).toBeInTheDocument()
    expect(await screen.findByText('Nenhum produto encontrado com esses filtros.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Limpar filtros' }))
    expect(await screen.findByRole('heading', { name: 'Trena a laser 40m' })).toBeInTheDocument()
  })

  it('sorts by price and filters free shipping', async () => {
    const { user } = renderApp('/')
    await screen.findByRole('heading', { name: 'Trena a laser 40m' })

    await user.selectOptions(screen.getByRole('combobox', { name: 'Ordenar por' }), 'Menor preço')
    await waitForOrder(['Jogo de chaves combinadas', 'Furadeira de impacto 700W', 'Trena a laser 40m'])

    await user.click(screen.getByRole('checkbox', { name: 'Somente frete grátis' }))
    await waitForOrder(['Furadeira de impacto 700W'])
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
