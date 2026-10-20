import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { cartItem } from '../test/cart'
import { nextShipmentAdvanceConflicts, seedFakeOrder, shipmentAt } from '../test/orders'
import { sampleProducts } from '../test/products'
import { renderApp } from '../test/render'
import { buyerUserId, sellerUserId } from '../test/tokens'

const drill = sampleProducts[2] // 199,90
const wrench = sampleProducts[1] // 89,50

/** Pedido do João com a furadeira da Maria (vendedora logada) e as chaves de outro vendedor. */
function seedSale(fields: Parameters<typeof seedFakeOrder>[2] = {}) {
  return seedFakeOrder(
    buyerUserId,
    [{ ...cartItem(drill, 2), sellerId: sellerUserId }, { ...cartItem(wrench, 1), sellerId: 'other-seller' }],
    { status: 'Confirmed', createdAt: '2026-10-14T10:00:00Z', ...fields },
  )
}

const shortId = (id: string) => `#${id.slice(0, 8).toUpperCase()}`

describe('ShipmentsPage', () => {
  it('is in the header only for sellers', async () => {
    renderApp('/', { loggedIn: true })
    expect(await within(screen.getByRole('banner')).findByRole('link', { name: 'Despachar' })).toHaveAttribute(
      'href',
      '/vender/envios',
    )
  })

  it('is not in the header of an account that only buys', async () => {
    renderApp('/', { loggedIn: 'buyer' })
    await within(screen.getByRole('banner')).findByRole('link', { name: 'Pedidos' })
    expect(within(screen.getByRole('banner')).queryByRole('link', { name: 'Despachar' })).not.toBeInTheDocument()
  })

  it('invites an account that only buys to start selling', async () => {
    renderApp('/vender/envios', { loggedIn: 'buyer' })

    expect(await screen.findByRole('heading', { name: 'Ative a venda na sua conta' })).toBeInTheDocument()
  })

  it('says when there is nothing to ship', async () => {
    seedFakeOrder(buyerUserId, [cartItem(drill, 1)], { status: 'Pending' })
    renderApp('/vender/envios', { loggedIn: true })

    expect(await screen.findByText('Nenhum pedido para despachar ainda.')).toBeInTheDocument()
  })

  it("lists the seller's part of each confirmed order", async () => {
    const order = seedSale()
    renderApp('/vender/envios', { loggedIn: true })

    const card = await screen.findByRole('article', { name: `Pedido ${shortId(order.id)}` })
    expect(within(card).getByText('Aguardando separação')).toBeInTheDocument()
    expect(within(card).getByText(/Furadeira de impacto 700W/)).toBeInTheDocument()
    expect(within(card).queryByText(/Jogo de chaves/)).not.toBeInTheDocument()
    expect(within(card).getByText(/R\$\s399,80/)).toBeInTheDocument()
  })

  it('moves the shipment step by step until it is shipped', async () => {
    const order = seedSale()
    const { user } = renderApp('/vender/envios', { loggedIn: true })
    const toShip = await screen.findByRole('region', { name: 'A despachar' })
    const card = await within(toShip).findByRole('article', { name: `Pedido ${shortId(order.id)}` })

    await user.click(within(card).getByRole('button', { name: 'Marcar como separado' }))
    expect(await within(card).findByText('Separado')).toBeInTheDocument()

    await user.click(within(card).getByRole('button', { name: 'Marcar como embalado' }))
    expect(await within(card).findByText('Embalado')).toBeInTheDocument()

    await user.click(within(card).getByRole('button', { name: 'Marcar como enviado' }))

    const shipped = await screen.findByRole('region', { name: 'Enviados' })
    const moved = await within(shipped).findByRole('article', { name: `Pedido ${shortId(order.id)}` })
    expect(within(moved).getByText('Enviado')).toBeInTheDocument()
    expect(within(moved).getByText('Aguardando o comprador confirmar o recebimento.')).toBeInTheDocument()
    expect(within(moved).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows when the buyer received it', async () => {
    const order = seedSale({ shipments: [shipmentAt(sellerUserId, 'Delivered', '2026-10-16T18:30:00Z'), shipmentAt('other-seller', 'AwaitingPicking')] })
    renderApp('/vender/envios', { loggedIn: true })

    const card = await screen.findByRole('article', { name: `Pedido ${shortId(order.id)}` })
    expect(within(card).getByText('Entregue')).toBeInTheDocument()
    expect(within(card).getByText(/Recebido pelo comprador em 16\/10\/2026/)).toBeInTheDocument()
  })

  it('warns and reloads when someone else changed the shipment', async () => {
    const order = seedSale()
    nextShipmentAdvanceConflicts()
    const { user } = renderApp('/vender/envios', { loggedIn: true })
    const card = await screen.findByRole('article', { name: `Pedido ${shortId(order.id)}` })

    await user.click(within(card).getByRole('button', { name: 'Marcar como separado' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Este envio foi alterado em outro lugar')
    expect(await screen.findByRole('button', { name: 'Marcar como separado' })).toBeEnabled()
  })
})
