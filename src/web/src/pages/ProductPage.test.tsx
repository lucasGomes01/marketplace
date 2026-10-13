import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { resetFakeProducts, sampleProducts } from '../test/products'
import { server } from '../test/server'
import { sellerUserId } from '../test/tokens'

const drill = sampleProducts[2] // estoque 5
const wrench = sampleProducts[1] // estoque 2
const laser = sampleProducts[0] // esgotado

const header = () => screen.getByRole('banner')

describe('ProductPage', () => {
  it('shows the product details', async () => {
    renderApp(`/produtos/${wrench.id}`)

    expect(await screen.findByRole('heading', { name: 'Jogo de chaves combinadas' })).toBeInTheDocument()
    expect(screen.getByText(/R\$\s89,50/)).toBeInTheDocument()
    expect(screen.getByText('12 peças, aço cromo-vanádio')).toBeInTheDocument()
    expect(screen.getByText('Últimas 2 unidades')).toBeInTheDocument()
  })

  it('tells when the product does not exist', async () => {
    renderApp('/produtos/nao-existe')

    expect(await screen.findByRole('heading', { name: 'Produto não encontrado' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver todos os produtos' })).toHaveAttribute('href', '/')
  })

  it('adds to the cart and updates the header counter', async () => {
    const { user } = renderApp(`/produtos/${drill.id}`, { loggedIn: 'buyer' })
    await screen.findByRole('heading', { name: drill.name })
    expect(await within(header()).findByRole('link', { name: 'Carrinho (0)' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Adicionar ao carrinho' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Adicionado ao carrinho')
    expect(screen.getByRole('link', { name: 'Ver carrinho' })).toHaveAttribute('href', '/carrinho')
    expect(within(header()).getByRole('link', { name: 'Carrinho (1)' })).toBeInTheDocument()
  })

  it('sends anonymous people to log in and back', async () => {
    const { user } = renderApp(`/produtos/${drill.id}`)

    await user.click(await screen.findByRole('button', { name: 'Adicionar ao carrinho' }))
    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument()
    await user.type(screen.getByLabelText('Email'), 'joao@example.com')
    await user.type(screen.getByLabelText('Senha'), 'Sup3r-secret')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('heading', { name: drill.name })).toBeInTheDocument()
  })

  it('does not let people add a sold out product', async () => {
    renderApp(`/produtos/${laser.id}`, { loggedIn: 'buyer' })

    expect(await screen.findByRole('button', { name: 'Esgotado' })).toBeDisabled()
  })

  it('does not let sellers buy their own product', async () => {
    resetFakeProducts([{ ...drill, sellerId: sellerUserId }])
    renderApp(`/produtos/${drill.id}`, { loggedIn: true })

    expect(await screen.findByRole('button', { name: 'Este produto é seu' })).toBeDisabled()
  })

  it('explains stock errors in Portuguese', async () => {
    const { user } = renderApp(`/produtos/${wrench.id}`, { loggedIn: 'buyer' })
    const add = await screen.findByRole('button', { name: 'Adicionar ao carrinho' })

    await user.click(add)
    await screen.findByRole('status')
    await user.click(add)
    await user.click(add)

    expect(await screen.findByRole('alert')).toHaveTextContent('Só há 2 em estoque.')
  })

  it('warns when the catalog is unavailable', async () => {
    server.use(
      http.post('/api/cart/items', () =>
        HttpResponse.json({ status: 503, title: 'The catalog is unavailable.', code: 'catalog.unavailable' }, { status: 503 }),
      ),
    )
    const { user } = renderApp(`/produtos/${drill.id}`, { loggedIn: 'buyer' })

    await user.click(await screen.findByRole('button', { name: 'Adicionar ao carrinho' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('indisponível')
  })
})
