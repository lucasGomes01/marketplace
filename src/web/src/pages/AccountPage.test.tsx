import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { loadSession } from '../auth/session'
import { renderApp } from '../test/render'
import { existingUser } from '../test/server'

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
    expect(screen.getByText('Vendedor')).toBeInTheDocument()
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
