import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { existingUser } from '../test/server'

describe('LoginPage', () => {
  it('logs in and shows the person name in the header', async () => {
    const { user } = renderApp('/entrar')

    await user.type(screen.getByLabelText('Email'), existingUser.email)
    await user.type(screen.getByLabelText('Senha'), existingUser.password)
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('heading', { name: 'Minha conta' })).toBeInTheDocument()
    expect(screen.getByRole('banner')).toHaveTextContent('Maria Silva')
    expect(screen.getByRole('button', { name: 'Sair' })).toBeInTheDocument()
  })

  it('shows a generic message for invalid credentials', async () => {
    const { user } = renderApp('/entrar')

    await user.type(screen.getByLabelText('Email'), existingUser.email)
    await user.type(screen.getByLabelText('Senha'), 'wrong-password')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Email ou senha inválidos.')
    expect(screen.queryByRole('heading', { name: 'Minha conta' })).not.toBeInTheDocument()
  })

  it('returns to the protected page the person tried to open', async () => {
    const { user } = renderApp('/minha-conta')

    await user.type(await screen.findByLabelText('Email'), existingUser.email)
    await user.type(screen.getByLabelText('Senha'), existingUser.password)
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('heading', { name: 'Minha conta' })).toBeInTheDocument()
  })
})
