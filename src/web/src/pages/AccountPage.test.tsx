import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { loadSession } from '../auth/session'
import { renderApp } from '../test/render'
import { buyerToken, buyerUser, existingUser } from '../test/server'

describe('AccountPage', () => {
  it('redirects to login when there is no session', async () => {
    renderApp('/minha-conta')

    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('shows the logged user data after a page reload (session in storage)', async () => {
    renderApp('/minha-conta', { loggedIn: true })

    expect(await screen.findByRole('heading', { name: 'Minha conta' })).toBeInTheDocument()
    expect(screen.getByText(existingUser.name, { selector: 'dd' })).toBeInTheDocument()
    expect(screen.getByText(existingUser.email)).toBeInTheDocument()
    expect(screen.getByText('Comprador e vendedor')).toBeInTheDocument()
  })

  it('does not offer to start selling to who already sells', async () => {
    renderApp('/minha-conta', { loggedIn: true })

    await screen.findByRole('heading', { name: 'Minha conta' })
    expect(screen.queryByRole('button', { name: 'Começar a vender' })).not.toBeInTheDocument()
  })

  it('lets a buyer start selling with the same account', async () => {
    const { user } = renderApp('/minha-conta', { loggedIn: 'buyer' })
    expect(await screen.findByText(buyerUser.name, { selector: 'dd' })).toBeInTheDocument()
    expect(screen.getByText('Comprador', { selector: 'dd *' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Começar a vender' }))

    expect(await screen.findByText('Comprador e vendedor')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Pronto! Agora você também pode vender.')
    expect(screen.queryByRole('button', { name: 'Começar a vender' })).not.toBeInTheDocument()
    // A sessão passa a usar o token novo, que tem o papel de vendedor.
    expect(loadSession()?.accessToken).not.toBe(buyerToken)
    expect(loadSession()?.accessToken).toBe(`seller-token-${buyerUser.id}`)
  })

  it('logs out and goes back home', async () => {
    const { user } = renderApp('/minha-conta', { loggedIn: true })

    await user.click(await screen.findByRole('button', { name: 'Sair' }))

    expect(await screen.findByRole('heading', { name: 'Compre e venda direto de quem faz.' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Entrar' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Entrar' })).toBeInTheDocument()
    expect(loadSession()).toBeNull()
  })

  it('drops a session the API no longer accepts', async () => {
    localStorage.setItem(
      'marketplace.session',
      JSON.stringify({ accessToken: 'revoked', expiresAt: new Date(Date.now() + 60_000).toISOString() }),
    )

    renderApp('/minha-conta')

    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument()
    expect(loadSession()).toBeNull()
  })
})
