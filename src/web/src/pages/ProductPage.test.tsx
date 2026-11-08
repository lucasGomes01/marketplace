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

  it('shows the listing details: discount, shipping, warranty, specs and the category path', async () => {
    renderApp(`/produtos/${drill.id}`)

    expect(await screen.findByRole('heading', { level: 1, name: drill.name })).toBeInTheDocument()
    expect(screen.getByText('20% OFF')).toBeInTheDocument()
    expect(screen.getByText('Frete grátis', { selector: '.perks strong' })).toBeInTheDocument()
    expect(screen.getByText('Garantia de 12 meses do vendedor')).toBeInTheDocument()
    expect(within(screen.getByRole('navigation', { name: 'Você está em' })).getByRole('link', { name: 'Ferramentas' })).toHaveAttribute(
      'href',
      '/?categoria=ferramentas',
    )

    const specs = within(screen.getByRole('table'))
    expect(specs.getByRole('rowheader', { name: 'Marca' }).nextSibling).toHaveTextContent('Bosch')
    expect(specs.getByRole('rowheader', { name: 'Voltagem' }).nextSibling).toHaveTextContent('220V')
    expect(specs.getByRole('rowheader', { name: 'Dimensões da embalagem' }).nextSibling).toHaveTextContent('30 × 25 × 10 cm')
    expect(specs.getByRole('rowheader', { name: 'Peso' }).nextSibling).toHaveTextContent('1,8 kg')
  })

  it('suggests other products of the same category', async () => {
    renderApp(`/produtos/${drill.id}`)

    const related = await screen.findByRole('region', { name: 'Você também pode gostar' })
    expect(within(related).getAllByRole('article').map((card) => within(card).getByRole('heading').textContent)).toEqual([
      'Trena a laser 40m',
      'Jogo de chaves combinadas',
    ])
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

  it('keeps the added item when the initial cart load answers late', async () => {
    // Bug achado pelo E2E: a carga inicial do carrinho (vazio) chegava depois do "adicionar" e apagava o item.
    let releaseInitialLoad = () => {}
    const initialLoad = new Promise<void>((resolve) => (releaseInitialLoad = resolve))
    server.use(
      http.get('/api/cart', async () => {
        await initialLoad
        return HttpResponse.json({ items: [], totalQuantity: 0, total: 0, currency: 'BRL' })
      }, { once: true }),
    )
    const { user } = renderApp(`/produtos/${drill.id}`, { loggedIn: 'buyer' })
    await screen.findByRole('heading', { name: drill.name })
    await within(header()).findByRole('button', { name: 'Sair' })

    await user.click(screen.getByRole('button', { name: 'Adicionar ao carrinho' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Adicionado ao carrinho')
    releaseInitialLoad()
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(within(header()).getByRole('link', { name: 'Carrinho (1)' })).toBeInTheDocument()
  })

  it('waits for the session before letting people add to the cart', async () => {
    // Bug achado pelo E2E: clicar enquanto a sessão ainda carregava mandava para o login e o item se perdia.
    let releaseSession = () => {}
    const session = new Promise<void>((resolve) => (releaseSession = resolve))
    server.use(
      http.get('/api/users/me', async ({ request }) => {
        await session
        const passthrough = await fetch(new Request(request.url, { headers: request.headers }))
        return passthrough
      }, { once: true }),
    )
    const { user } = renderApp(`/produtos/${drill.id}`, { loggedIn: 'buyer' })
    await screen.findByRole('heading', { name: drill.name })

    expect(screen.getByRole('button', { name: 'Adicionar ao carrinho' })).toBeDisabled()

    releaseSession()
    await within(header()).findByRole('button', { name: 'Sair' })
    await user.click(screen.getByRole('button', { name: 'Adicionar ao carrinho' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Adicionado ao carrinho')
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
