import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'

describe('ProductPage', () => {
  it('shows the product details', async () => {
    renderApp('/produtos/p2-0000-0000-0000-000000000002')

    expect(await screen.findByRole('heading', { name: 'Jogo de chaves combinadas' })).toBeInTheDocument()
    expect(screen.getByText(/R\$\s89,50/)).toBeInTheDocument()
    expect(screen.getByText('12 peças, aço cromo-vanádio')).toBeInTheDocument()
    expect(screen.getByText('Últimas 2 unidades')).toBeInTheDocument()
  })

  it('has the cart button disabled until the cart exists', async () => {
    renderApp('/produtos/p1-0000-0000-0000-000000000001')

    expect(await screen.findByRole('button', { name: 'Carrinho em breve' })).toBeDisabled()
  })

  it('tells when the product does not exist', async () => {
    renderApp('/produtos/nao-existe')

    expect(await screen.findByRole('heading', { name: 'Produto não encontrado' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver todos os produtos' })).toHaveAttribute('href', '/')
  })
})
