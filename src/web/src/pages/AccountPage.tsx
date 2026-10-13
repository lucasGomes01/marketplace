import { roleLabels } from '../api/users'
import { useAuth } from '../auth/context'

export function AccountPage() {
  const { state } = useAuth()
  if (state.status !== 'authenticated') {
    return null
  }
  const { user } = state

  return (
    <section className="panel">
      <h1>Minha conta</h1>
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
            <span className={`badge badge--${user.role.toLowerCase()}`}>{roleLabels[user.role]}</span>
          </dd>
        </div>
      </dl>
    </section>
  )
}
