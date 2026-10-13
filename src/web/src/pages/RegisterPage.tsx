import { useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router'
import { ApiError, type FieldErrors } from '../api/client'
import { translateFieldErrors } from '../api/messages'
import { usersApi } from '../api/users'
import { useAuth } from '../auth/context'
import { FormField } from '../components/FormField'

type FormError = { kind: 'duplicate' } | { kind: 'generic'; message: string } | null

export function RegisterPage() {
  const { state, login } = useAuth()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<FormError>(null)
  const [submitting, setSubmitting] = useState(false)

  if (state.status === 'authenticated' && !submitting) {
    return <Navigate to="/minha-conta" replace />
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    setFieldErrors({})
    setFormError(null)

    try {
      await usersApi.register({ name, email, password })
      await login(email, password)
      navigate('/minha-conta', { replace: true })
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setFormError({ kind: 'duplicate' })
      } else if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        setFieldErrors(translateFieldErrors(error.fieldErrors))
      } else {
        setFormError({ kind: 'generic', message: 'Não foi possível criar a conta agora. Tente novamente.' })
      }
      setSubmitting(false)
    }
  }

  return (
    <section className="auth-card">
      <h1>Criar conta</h1>
      <p className="muted">Com a mesma conta você compra e, quando quiser, começa a vender.</p>

      {formError ? (
        <div className="alert alert--error" role="alert">
          {formError.kind === 'duplicate' ? (
            <>
              Já existe uma conta com este email. <Link to="/entrar">Entrar</Link>
            </>
          ) : (
            formError.message
          )}
        </div>
      ) : null}

      <form onSubmit={handleSubmit} className="form">
        <FormField
          label="Nome"
          name="name"
          autoComplete="name"
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          error={fieldErrors.name}
        />
        <FormField
          label="Email"
          name="email"
          type="email"
          autoComplete="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={fieldErrors.email}
        />
        <FormField
          label="Senha"
          name="password"
          type="password"
          autoComplete="new-password"
          required
          hint="Mínimo de 8 caracteres."
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={fieldErrors.password}
        />


        <button type="submit" className="button button--primary button--block" disabled={submitting}>
          {submitting ? 'Criando conta…' : 'Criar conta'}
        </button>
      </form>

      <p className="auth-card__footer">
        Já tem conta? <Link to="/entrar">Entrar</Link>
      </p>
    </section>
  )
}
