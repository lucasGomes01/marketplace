import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { seedPayout, seedPayoutCard } from '../test/payments'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const header = () => screen.getByRole('banner')
const shortId = (id: string) => `#${id.slice(0, 8).toUpperCase()}`

describe('PayoutsPage', () => {
  it('is in the header only for sellers', async () => {
    renderApp('/', { loggedIn: true })
    expect(await within(header()).findByRole('link', { name: 'Repasses' })).toHaveAttribute('href', '/vender/repasses')
  })

  it('is not in the header of an account that only buys', async () => {
    renderApp('/', { loggedIn: 'buyer' })
    await within(header()).findByRole('link', { name: 'Pedidos' })
    expect(within(header()).queryByRole('link', { name: 'Repasses' })).not.toBeInTheDocument()
  })

  it('invites an account that only buys to start selling', async () => {
    renderApp('/vender/repasses', { loggedIn: 'buyer' })

    expect(await screen.findByRole('heading', { name: 'Repasses' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /começar a vender/i })).toBeInTheDocument()
  })

  it('asks a new seller for a Mastercard and explains when the money comes', async () => {
    renderApp('/vender/repasses', { loggedIn: true })

    expect(await screen.findByRole('heading', { name: 'Cartão de recebimento' })).toBeInTheDocument()
    expect(screen.getByText(/Mastercard Send/)).toBeInTheDocument()
    expect(screen.getByLabelText('Número do cartão Mastercard')).toBeInTheDocument()
    expect(screen.getByText(/Nenhuma venda ainda/)).toBeInTheDocument()
  })

  it('formats the number while typing and validates before sending', async () => {
    const { user } = renderApp('/vender/repasses', { loggedIn: true })
    const number = await screen.findByLabelText('Número do cartão Mastercard')

    await user.type(number, '4111111111111111')
    expect(number).toHaveValue('4111 1111 1111 1111')
    await user.click(screen.getByRole('button', { name: 'Salvar cartão' }))

    expect(screen.getByText('Só aceitamos cartões Mastercard para receber.')).toBeInTheDocument()
    expect(screen.getByText('Informe a validade como MM/AA.')).toBeInTheDocument()
    expect(screen.getByText('Informe o nome impresso no cartão.')).toBeInTheDocument()
  })

  it('saves the card and shows only the last digits', async () => {
    const { user } = renderApp('/vender/repasses', { loggedIn: true })

    await user.type(await screen.findByLabelText('Número do cartão Mastercard'), '5555555555554444')
    await user.type(screen.getByLabelText('Validade'), '12/29')
    await user.type(screen.getByLabelText('Nome impresso no cartão'), 'Maria Silva')
    await user.click(screen.getByRole('button', { name: 'Salvar cartão' }))

    expect(await screen.findByText('Cartão salvo. Os repasses liberados serão pagos nele.')).toBeInTheDocument()
    expect(await screen.findByText('Mastercard •••• 4444')).toBeInTheDocument()
    expect(screen.queryByText(/5555 5555/)).not.toBeInTheDocument()
  })

  it('shows the server message translated when the card is refused', async () => {
    server.use(
      http.put('/api/payouts/card', () =>
        HttpResponse.json({ status: 400, errors: { expiry: ['Card is expired.'] } }, { status: 400 }),
      ),
    )
    const { user } = renderApp('/vender/repasses', { loggedIn: true })

    await user.type(await screen.findByLabelText('Número do cartão Mastercard'), '5555555555554444')
    await user.type(screen.getByLabelText('Validade'), '01/27')
    await user.type(screen.getByLabelText('Nome impresso no cartão'), 'Maria')
    await user.click(screen.getByRole('button', { name: 'Salvar cartão' }))

    expect(await screen.findByText('Este cartão está vencido.')).toBeInTheDocument()
  })

  it('lets the seller replace the registered card', async () => {
    seedPayoutCard({ last4: '4444' })
    const { user } = renderApp('/vender/repasses', { loggedIn: true })

    await user.click(await screen.findByRole('button', { name: 'Trocar cartão' }))
    await user.type(screen.getByLabelText('Número do cartão Mastercard'), '2223000048400011')
    await user.type(screen.getByLabelText('Validade'), '01/31')
    await user.type(screen.getByLabelText('Nome impresso no cartão'), 'Maria S')
    await user.click(screen.getByRole('button', { name: 'Salvar cartão' }))

    expect(await screen.findByText('Mastercard •••• 0011')).toBeInTheDocument()
  })

  it('lists each sale with fee, net amount and what happens next', async () => {
    seedPayoutCard()
    const sent = seedPayout({ status: 'Sent', gross: 200, fee: 20, net: 180, sentAt: '2026-11-02T10:00:00Z' })
    const waiting = seedPayout({ status: 'AwaitingDelivery', gross: 89.5, fee: 8.95, net: 80.55 })
    seedPayout({ status: 'Failed', failureReason: 'RECEIVER_INELIGIBLE' })
    renderApp('/vender/repasses', { loggedIn: true })

    const sentCard = await screen.findByRole('article', { name: `Pedido ${shortId(sent.orderId)}` })
    expect(within(sentCard).getByText('Pago')).toBeInTheDocument()
    expect(within(sentCard).getByText(/R\$\s180,00/)).toBeInTheDocument()
    expect(within(sentCard).getByText('Pago no seu cartão em 02/11/2026.')).toBeInTheDocument()

    const waitingCard = screen.getByRole('article', { name: `Pedido ${shortId(waiting.orderId)}` })
    expect(within(waitingCard).getByText(/R\$\s8,95/)).toBeInTheDocument()
    expect(within(waitingCard).getByText(/R\$\s80,55/)).toBeInTheDocument()
    expect(within(waitingCard).getByText('Liberado quando o comprador confirmar o recebimento.')).toBeInTheDocument()

    expect(screen.getByText(/recusou o pagamento \(RECEIVER_INELIGIBLE\)/)).toBeInTheDocument()

    const totals = screen.getByRole('region', { name: 'Totais' })
    expect(within(totals).getByText(/R\$\s170,55/)).toBeInTheDocument() // a receber: 80,55 + 90
    expect(within(totals).getByText(/R\$\s180,00/)).toBeInTheDocument() // recebido
  })

  it('tells the seller to register a card when a delivered sale waits for it', async () => {
    seedPayout({ status: 'AwaitingCard', deliveredAt: '2026-11-02T10:00:00Z' })
    const { user } = renderApp('/vender/repasses', { loggedIn: true })

    expect(await screen.findByText('Entregue! Cadastre um cartão Mastercard para receber.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Número do cartão Mastercard'), '5555555555554444')
    await user.type(screen.getByLabelText('Validade'), '12/29')
    await user.type(screen.getByLabelText('Nome impresso no cartão'), 'Maria Silva')
    await user.click(screen.getByRole('button', { name: 'Salvar cartão' }))

    expect(await screen.findByText(/Pagamento previsto para/)).toBeInTheDocument()
  })

  it('tells when the payouts cannot be loaded', async () => {
    server.use(http.get('/api/payouts', () => new HttpResponse(null, { status: 503 })))
    renderApp('/vender/repasses', { loggedIn: true })

    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível carregar seus repasses')
  })
})
