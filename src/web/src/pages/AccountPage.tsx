import { useState } from 'react'
import { accountTypeLabel, isSeller } from '../api/users'
import { useAuth } from '../auth/context'

export function AccountPage() {
  const { state, becomeSeller } = useAuth()
  const [activating, setActivating] = useState(false)
  const [message, setMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null)

  if (state.status !== 'authenticated') {
    return null
  }
  const { user } = state

  async function handleBecomeSeller() {
    setActivating(true)
    setMessage(null)
    try {
      await becomeSeller()
      setMessage({ kind: 'success', text: 'Pronto! Agora você também pode vender.' })
    } catch {
      setMessage({ kind: 'error', text: 'Não foi possível ativar a venda agora. Tente novamente.' })
    } finally {
      setActivating(false)
    }
  }

  return (
    <section className="panel">
      <h1>Minha conta</h1>

      {message ? (
        <div className={`alert alert--${message.kind}`} role={message.kind === 'error' ? 'alert' : 'status'}>
          {message.text}
        </div>
      ) : null}

      <dl className="details">
        <div>
          <dt>Nome</dt>
          <dd>{user.name}</dd>
        </div>
        <div>
          <dt>Email</dt>
          <dd>{user.email}</dd>
        </div>
        <div>
          <dt>Tipo de conta</dt>
          <dd>
            <span className={`badge ${isSeller(user) ? 'badge--seller' : 'badge--buyer'}`}>{accountTypeLabel(user)}</span>
          </dd>
        </div>
      </dl>

      {!isSeller(user) ? (
        <div className="callout">
          <div>
            <h2>Quer vender no Marketplace?</h2>
            <p className="muted">Ative a venda na sua conta. Você continua comprando normalmente.</p>
          </div>
          <button type="button" className="button button--primary" onClick={handleBecomeSeller} disabled={activating}>
            {activating ? 'Ativando…' : 'Começar a vender'}
          </button>
        </div>
      ) : null}
    </section>
  )
}
