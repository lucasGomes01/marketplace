import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { existingUser, server } from '../test/server'

async function fillForm(
  user: ReturnType<typeof renderApp>['user'],
  { name = 'Maria Silva', email = existingUser.email, password = existingUser.password, seller = true } = {},
) {
  await user.type(screen.getByLabelText('Nome'), name)
  await user.type(screen.getByLabelText('Email'), email)
  await user.type(screen.getByLabelText('Senha'), password)
  if (seller) {
    await user.click(screen.getByLabelText(/Vendedor/))
  }
}

describe('RegisterPage', () => {
  it('registers, logs in automatically and shows the account page', async () => {
    let registered: unknown
    server.use(
      http.post('/api/users', async ({ request }) => {
        registered = await request.json()
        return HttpResponse.json({ id: existingUser.id, email: existingUser.email, name: 'Maria Silva', role: 'Seller' }, { status: 201 })
      }),
    )
    const { user } = renderApp('/cadastro')

    await fillForm(user)
    await user.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(await screen.findByRole('heading', { name: 'Minha conta' })).toBeInTheDocument()
    expect(registered).toEqual({ name: 'Maria Silva', email: existingUser.email, password: existingUser.password, role: 'Seller' })
  })

  it('creates a buyer account by default', async () => {
    let role: string | undefined
    server.use(
      http.post('/api/users', async ({ request }) => {
        role = ((await request.json()) as { role: string }).role
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user } = renderApp('/cadastro')

    await fillForm(user, { seller: false })
    await user.click(screen.getByRole('button', { name: 'Criar conta' }))

    await screen.findByRole('heading', { name: 'Minha conta' })
    expect(role).toBe('Buyer')
  })

  it('shows validation errors below each field', async () => {
    server.use(
      http.post('/api/users', () =>
        HttpResponse.json(
          { status: 400, errors: { password: ['Password must have at least 8 characters.'] } },
          { status: 400 },
        ),
      ),
    )
    const { user } = renderApp('/cadastro')

    await fillForm(user, { password: 'short' })
    await user.click(screen.getByRole('button', { name: 'Criar conta' }))

    const password = screen.getByLabelText('Senha')
    expect(await screen.findByText('A senha precisa ter pelo menos 8 caracteres.')).toBeInTheDocument()
    expect(password).toHaveAttribute('aria-invalid', 'true')
    expect(password).toHaveAccessibleDescription('A senha precisa ter pelo menos 8 caracteres.')
  })

  it('tells the person when the email is already registered', async () => {
    const { user } = renderApp('/cadastro')

    await fillForm(user, { email: 'MARIA@example.com' })
    await user.click(screen.getByRole('button', { name: 'Criar conta' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Já existe uma conta com este email.')
    expect(within(alert).getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/entrar')
  })

  it('disables the button while submitting', async () => {
    let release: () => void = () => {}
    server.use(
      http.post('/api/users', async () => {
        await new Promise<void>((resolve) => (release = resolve))
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user } = renderApp('/cadastro')

    await fillForm(user)
    await user.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(await screen.findByRole('button', { name: 'Criando conta…' })).toBeDisabled()
    release()
  })
})
