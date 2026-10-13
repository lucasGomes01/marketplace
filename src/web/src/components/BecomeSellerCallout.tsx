import { useState } from 'react'
import { useAuth } from '../auth/context'

interface Props {
  title: string
  description: string
  /** Chamado depois que o perfil de vendedor foi ativado (a sessão já tem o token novo). */
  onActivated?: () => void
}

/** Convite para a conta que só compra ativar a venda, sem sair e entrar de novo. */
export function BecomeSellerCallout({ title, description, onActivated }: Props) {
  const { becomeSeller } = useAuth()
  const [activating, setActivating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleClick() {
    setActivating(true)
    setError(null)
    try {
      await becomeSeller()
      onActivated?.()
    } catch {
      setError('Não foi possível ativar a venda agora. Tente novamente.')
      setActivating(false)
    }
  }

  return (
    <div className="callout">
      <div>
        <h2>{title}</h2>
        <p className="muted">{description}</p>
        {error ? (
          <p className="field__error" role="alert">
            {error}
          </p>
        ) : null}
      </div>
      <button type="button" className="button button--primary" onClick={handleClick} disabled={activating}>
        {activating ? 'Ativando…' : 'Começar a vender'}
      </button>
    </div>
  )
}
