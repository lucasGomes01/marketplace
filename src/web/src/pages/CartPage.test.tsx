import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { CartItem } from '../api/cart'
import { seedFakeCart } from '../test/cart'
import { sampleProducts } from '../test/products'
import { renderApp } from '../test/render'
import { buyerUserId } from '../test/tokens'

const drill = sampleProducts[2] // 199,90 · estoque 5
const wrench = sampleProducts[1] // 89,50 · estoque 2

const item = (product: (typeof sampleProducts)[number], quantity: number): CartItem => ({
  productId: product.id,
  sellerId: product.sellerId,
  productName: product.name,
  unitPrice: product.price,
  currency: product.currency,
  quantity,
  subtotal: Math.round(product.price * quantity * 100) / 100,
})

const row = (name: string) => screen.getByRole('listitem', { name })

describe('CartPage', () => {
  it('requires login', async () => {
    renderApp('/carrinho')

    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('shows an empty cart', async () => {
    renderApp('/carrinho', { loggedIn: 'buyer' })

    expect(await screen.findByText('Seu carrinho está vazio.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver produtos' })).toHaveAttribute('href', '/')
  })

  it('lists items with subtotals and the total', async () => {
    seedFakeCart(buyerUserId, [item(drill, 2), item(wrench, 1)])
    renderApp('/carrinho', { loggedIn: 'buyer' })

    await screen.findByRole('heading', { name: 'Carrinho' })
    const drillRow = row(drill.name)
    expect(within(drillRow).getByRole('link', { name: drill.name })).toHaveAttribute('href', `/produtos/${drill.id}`)
    expect(within(drillRow).getByText(/R\$\s399,80/)).toBeInTheDocument()
    expect(screen.getByText('3 itens')).toBeInTheDocument()
    expect(screen.getByTestId('cart-total')).toHaveTextContent(/R\$\s489,30/)
    expect(screen.getByRole('button', { name: /Finalizar pedido/ })).toBeDisabled()
  })

  it('increases and decreases quantities', async () => {
    seedFakeCart(buyerUserId, [item(drill, 1)])
    const { user } = renderApp('/carrinho', { loggedIn: 'buyer' })
    await screen.findByRole('heading', { name: 'Carrinho' })

    expect(screen.getByRole('button', { name: `Diminuir quantidade de ${drill.name}` })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: `Aumentar quantidade de ${drill.name}` }))

    expect(await within(row(drill.name)).findByText(/R\$\s399,80/)).toBeInTheDocument()
    expect(within(row(drill.name)).getByText('2', { selector: 'output' })).toBeInTheDocument()
    expect(within(screen.getByRole('banner')).getByRole('link', { name: 'Carrinho (2)' })).toBeInTheDocument()
  })

  it('shows a stock error without losing the cart', async () => {
    seedFakeCart(buyerUserId, [item(wrench, 2)])
    const { user } = renderApp('/carrinho', { loggedIn: 'buyer' })
    await screen.findByRole('heading', { name: 'Carrinho' })

    await user.click(screen.getByRole('button', { name: `Aumentar quantidade de ${wrench.name}` }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Só há 2 em estoque.')
    expect(within(row(wrench.name)).getByText('2', { selector: 'output' })).toBeInTheDocument()
  })

  it('removes an item', async () => {
    seedFakeCart(buyerUserId, [item(drill, 1), item(wrench, 1)])
    const { user } = renderApp('/carrinho', { loggedIn: 'buyer' })
    await screen.findByRole('heading', { name: 'Carrinho' })

    await user.click(within(row(drill.name)).getByRole('button', { name: 'Remover' }))

    expect(await screen.findByText('1 item')).toBeInTheDocument()
    expect(screen.queryByRole('listitem', { name: drill.name })).not.toBeInTheDocument()
  })

  it('is reachable from the header and the counter resets on logout', async () => {
    seedFakeCart(buyerUserId, [item(drill, 2)])
    const { user } = renderApp('/', { loggedIn: 'buyer' })

    const link = await within(screen.getByRole('banner')).findByRole('link', { name: 'Carrinho (2)' })
    expect(link).toHaveAttribute('href', '/carrinho')

    await user.click(screen.getByRole('button', { name: 'Sair' }))

    expect(within(screen.getByRole('banner')).queryByRole('link', { name: /Carrinho/ })).not.toBeInTheDocument()
  })
})
