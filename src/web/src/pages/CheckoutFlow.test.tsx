import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { cartItem, seedFakeCart } from '../test/cart'
import { nextFakeOrderWill } from '../test/orders'
import { sampleProducts } from '../test/products'
import { renderApp } from '../test/render'
import { server } from '../test/server'
import { buyerUserId } from '../test/tokens'

const drill = sampleProducts[2] // 199,90 · estoque 5
const wrench = sampleProducts[1] // 89,50 · estoque 2

const header = () => within(screen.getByRole('banner'))

describe('checkout', () => {
  it('places the order, empties the cart and follows the order until it is confirmed', async () => {
    seedFakeCart(buyerUserId, [cartItem(drill, 2), cartItem(wrench, 1)])
    nextFakeOrderWill({ status: 'Confirmed' }, { notFoundReads: 2, pendingReads: 2 })
    const { user } = renderApp('/carrinho', { loggedIn: 'buyer' })

    await user.click(await screen.findByRole('button', { name: 'Finalizar pedido' }))

    expect(await screen.findByRole('heading', { name: 'Pedido recebido' })).toBeInTheDocument()
    expect(header().getByRole('link', { name: 'Carrinho (0)' })).toBeInTheDocument()
    expect(await screen.findByText('Confirmado')).toBeInTheDocument()
    expect(screen.getByText(/Seu pedido foi confirmado/)).toBeInTheDocument()
    expect(screen.getByRole('listitem', { name: drill.name })).toHaveTextContent(/2 × R\$\s199,90/)
    expect(screen.getByTestId('order-total')).toHaveTextContent(/R\$\s489,30/)
  })

  it('shows the reason when the order is rejected', async () => {
    seedFakeCart(buyerUserId, [cartItem(wrench, 2)])
    nextFakeOrderWill({ status: 'Rejected', reason: 'Estoque insuficiente: Jogo de chaves combinadas (pedido 2, disponível 1)' })
    const { user } = renderApp('/carrinho', { loggedIn: 'buyer' })

    await user.click(await screen.findByRole('button', { name: 'Finalizar pedido' }))

    expect(await screen.findByText('Recusado')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Estoque insuficiente: Jogo de chaves combinadas (pedido 2, disponível 1)',
    )
  })

  it('stops checking after a while and says the order is still being processed', async () => {
    seedFakeCart(buyerUserId, [cartItem(drill, 1)])
    nextFakeOrderWill({ status: 'Pending' })
    const { user } = renderApp('/carrinho', { loggedIn: 'buyer' })

    await user.click(await screen.findByRole('button', { name: 'Finalizar pedido' }))

    expect(await screen.findByText(/ainda está sendo processado/)).toBeInTheDocument()
    expect(screen.getByText('Processando')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver meus pedidos' })).toHaveAttribute('href', '/pedidos')
  })

  it.each([
    [409, {}, 'Seu carrinho mudou em outra aba. Recarregue a página e tente de novo.'],
    [503, {}, 'Não foi possível finalizar o pedido agora. Tente de novo em instantes.'],
    [400, { errors: { cart: ['The cart is empty.'] } }, 'Seu carrinho está vazio.'],
  ])('keeps the cart and explains a %i error', async (status, body, message) => {
    seedFakeCart(buyerUserId, [cartItem(drill, 1)])
    server.use(http.post('/api/cart/checkout', () => HttpResponse.json({ status, ...body }, { status })))
    const { user } = renderApp('/carrinho', { loggedIn: 'buyer' })

    await user.click(await screen.findByRole('button', { name: 'Finalizar pedido' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(message)
    expect(screen.getByRole('listitem', { name: drill.name })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Finalizar pedido' })).toBeEnabled()
  })
})
