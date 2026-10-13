import { useState } from 'react'
import { Link } from 'react-router'
import { accountTypeLabel, isSeller } from '../api/users'
import { useAuth } from '../auth/context'
import { BecomeSellerCallout } from '../components/BecomeSellerCallout'

export function AccountPage() {
  const { state } = useAuth()
  const [activated, setActivated] = useState(false)

  if (state.status !== 'authenticated') {
    return null
  }
  const { user } = state

  return (
    <section className="panel">
      <h1>Minha conta</h1>

      {activated ? (
        <div className="alert alert--success" role="status">
          Pronto! Agora você também pode vender. <Link to="/vender/novo">Anunciar um produto</Link>
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
        <BecomeSellerCallout
          title="Quer vender no Marketplace?"
          description="Ative a venda na sua conta. Você continua comprando normalmente."
          onActivated={() => setActivated(true)}
        />
      ) : null}
    </section>
  )
}
