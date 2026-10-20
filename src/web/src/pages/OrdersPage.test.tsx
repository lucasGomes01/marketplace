import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { cartItem } from '../test/cart'
import { seedFakeOrder, shipmentAt } from '../test/orders'
import { sampleProducts } from '../test/products'
import { renderApp } from '../test/render'
import { buyerUserId, sellerUserId } from '../test/tokens'

const drill = sampleProducts[2] // 199,90
const wrench = sampleProducts[1] // 89,50

describe('OrdersPage', () => {
  it('requires login', async () => {
    renderApp('/pedidos')

    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('invites to shop when there are no orders', async () => {
    renderApp('/pedidos', { loggedIn: 'buyer' })

    expect(await screen.findByText('Você ainda não fez nenhum pedido.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver produtos' })).toHaveAttribute('href', '/')
  })

  it('lists the orders, newest first, with status, item count and total', async () => {
    const older = seedFakeOrder(buyerUserId, [cartItem(drill, 2), cartItem(wrench, 1)], {
      status: 'Confirmed',
      createdAt: '2026-10-10T15:30:00Z',
    })
    const newer = seedFakeOrder(buyerUserId, [cartItem(wrench, 1)], {
      status: 'Rejected',
      rejectionReason: 'Estoque insuficiente',
      createdAt: '2026-10-12T09:00:00Z',
    })
    seedFakeOrder(sellerUserId, [cartItem(drill, 1)], { status: 'Confirmed' })
    renderApp('/pedidos', { loggedIn: 'buyer' })

    const rows = await screen.findAllByRole('listitem')
    expect(rows).toHaveLength(2)

    expect(within(rows[0]).getByText('Recusado')).toBeInTheDocument()
    expect(within(rows[0]).getByText('1 item')).toBeInTheDocument()
    expect(within(rows[0]).getByRole('link')).toHaveAttribute('href', `/pedidos/${newer.id}`)

    expect(within(rows[1]).getByText('Confirmado')).toBeInTheDocument()
    expect(within(rows[1]).getByText('3 itens')).toBeInTheDocument()
    expect(within(rows[1]).getByText(/R\$\s489,30/)).toBeInTheDocument()
    expect(within(rows[1]).getByText('10/10/2026')).toBeInTheDocument()
    expect(within(rows[1]).getByRole('link')).toHaveAttribute('href', `/pedidos/${older.id}`)
  })

  it('shows how the delivery of confirmed orders is going', async () => {
    seedFakeOrder(buyerUserId, [cartItem(drill, 1), { ...cartItem(wrench, 1), sellerId: 's2' }], {
      status: 'Confirmed',
      shipments: [shipmentAt('s1', 'Shipped'), shipmentAt('s2', 'Packed')],
    })
    renderApp('/pedidos', { loggedIn: 'buyer' })

    const [row] = await screen.findAllByRole('listitem')
    expect(within(row).getByText('Entrega: Parcialmente enviado')).toBeInTheDocument()
  })

  it('is reachable from the header', async () => {
    renderApp('/', { loggedIn: 'buyer' })

    expect(await within(screen.getByRole('banner')).findByRole('link', { name: 'Pedidos' })).toHaveAttribute(
      'href',
      '/pedidos',
    )
  })
})

describe('OrderPage', () => {
  it('shows an order that does not belong to the user as not found', async () => {
    const order = seedFakeOrder(sellerUserId, [cartItem(drill, 1)], { status: 'Confirmed' })
    renderApp(`/pedidos/${order.id}`, { loggedIn: 'buyer' })

    expect(await screen.findByRole('heading', { name: 'Pedido não encontrado' })).toBeInTheDocument()
  })

  it('opens a past order directly without the "received" greeting', async () => {
    const order = seedFakeOrder(buyerUserId, [cartItem(drill, 1)], { status: 'Confirmed' })
    renderApp(`/pedidos/${order.id}`, { loggedIn: 'buyer' })

    expect(await screen.findByText('Confirmado')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /^Pedido / })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Pedido recebido' })).not.toBeInTheDocument()
  })

  it('shows one shipment per seller with its timeline', async () => {
    const order = seedFakeOrder(buyerUserId, [cartItem(drill, 1), { ...cartItem(wrench, 1), sellerId: 's2' }], {
      status: 'Confirmed',
      shipments: [shipmentAt('s1', 'Packed', '2026-10-15T10:00:00Z'), shipmentAt('s2', 'AwaitingPicking')],
    })
    renderApp(`/pedidos/${order.id}`, { loggedIn: 'buyer' })

    const delivery = await screen.findByRole('region', { name: 'Entrega' })
    const first = within(delivery).getByRole('article', { name: 'Envio 1 de 2' })
    expect(within(first).getByText(/Furadeira de impacto 700W/)).toBeInTheDocument()
    expect(within(first).queryByText(/Jogo de chaves/)).not.toBeInTheDocument()
    const timeline = within(first).getByRole('list', { name: 'Andamento do envio' })
    const steps = within(timeline).getAllByRole('listitem')
    expect(steps.map((s) => s.textContent)).toEqual([
      expect.stringMatching(/^Separado.*15\/10\/2026/),
      expect.stringMatching(/^Embalado.*15\/10\/2026/),
      'Enviado',
      'Entregue',
    ])
    expect(within(first).queryByRole('button', { name: 'Recebi' })).not.toBeInTheDocument()

    const second = within(delivery).getByRole('article', { name: 'Envio 2 de 2' })
    expect(within(second).getByText('Aguardando separação')).toBeInTheDocument()
  })

  it('lets the buyer confirm a shipped shipment was received', async () => {
    const order = seedFakeOrder(buyerUserId, [cartItem(drill, 1)], {
      status: 'Confirmed',
      shipments: [shipmentAt('s1', 'Shipped')],
    })
    const { user } = renderApp(`/pedidos/${order.id}`, { loggedIn: 'buyer' })
    const shipment = await screen.findByRole('article', { name: 'Envio 1 de 1' })

    await user.click(within(shipment).getByRole('button', { name: 'Recebi' }))

    expect(await within(shipment).findByText('Entregue', { selector: '.badge' })).toBeInTheDocument()
    expect(within(shipment).queryByRole('button', { name: 'Recebi' })).not.toBeInTheDocument()
  })

  it('does not show deliveries of an order that was not confirmed', async () => {
    const order = seedFakeOrder(buyerUserId, [cartItem(drill, 1)], { status: 'Rejected', rejectionReason: 'Sem estoque' })
    renderApp(`/pedidos/${order.id}`, { loggedIn: 'buyer' })

    await screen.findByText('Recusado')
    expect(screen.queryByRole('region', { name: 'Entrega' })).not.toBeInTheDocument()
  })
})
