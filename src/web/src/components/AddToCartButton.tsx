import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { cartErrorMessage } from '../api/cart'
import type { Product } from '../api/products'
import { useAuth } from '../auth/context'
import { useCart } from '../cart/context'

/** Botão de compra da página do produto, com os casos: sem login, esgotado, produto próprio, sucesso e erro. */
export function AddToCartButton({ product }: { product: Product }) {
  const { state } = useAuth()
  const { add } = useCart()
  const navigate = useNavigate()
  const location = useLocation()
  const [adding, setAdding] = useState(false)
  const [message, setMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null)

  const ownProduct = state.status === 'authenticated' && state.user.id === product.sellerId
  if (ownProduct) {
    return <Disabled label="Este produto é seu" />
  }
  if (product.stock <= 0) {
    return <Disabled label="Esgotado" />
  }

  async function handleClick() {
    if (state.status !== 'authenticated') {
      navigate('/entrar', { state: { from: location.pathname } })
      return
    }
    setAdding(true)
    setMessage(null)
    try {
      await add(product.id)
      setMessage({ kind: 'success', text: 'Adicionado ao carrinho.' })
    } catch (error) {
      setMessage({ kind: 'error', text: cartErrorMessage(error) })
    } finally {
      setAdding(false)
    }
  }

  return (
    <div className="add-to-cart">
      {/* Enquanto a sessão carrega não dá para saber se é login ou compra: espera em vez de mandar para o login. */}
      <button
        type="button"
        className="button button--primary"
        onClick={handleClick}
        disabled={adding || state.status === 'loading'}
      >
        {adding ? 'Adicionando…' : 'Adicionar ao carrinho'}
      </button>
      {message?.kind === 'success' ? (
        <p className="add-to-cart__message add-to-cart__message--success" role="status">
          {message.text} <Link to="/carrinho">Ver carrinho</Link>
        </p>
      ) : null}
      {message?.kind === 'error' ? (
        <p className="add-to-cart__message field__error" role="alert">
          {message.text}
        </p>
      ) : null}
    </div>
  )
}

function Disabled({ label }: { label: string }) {
  return (
    <div className="add-to-cart">
      <button type="button" className="button button--primary" disabled>
        {label}
      </button>
    </div>
  )
}
