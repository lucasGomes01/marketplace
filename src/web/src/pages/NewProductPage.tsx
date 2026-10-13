import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, type FieldErrors } from '../api/client'
import { translateFieldErrors } from '../api/messages'
import { productsApi } from '../api/products'
import { isSeller } from '../api/users'
import { useAuth } from '../auth/context'
import { BecomeSellerCallout } from '../components/BecomeSellerCallout'
import { FormField, TextAreaField } from '../components/FormField'
import { parsePrice } from '../utils/money'

export function NewProductPage() {
  const { state } = useAuth()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [price, setPrice] = useState('')
  const [stock, setStock] = useState('1')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (state.status !== 'authenticated') {
    return null
  }

  if (!isSeller(state.user)) {
    return (
      <section className="panel">
        <h1>Anunciar um produto</h1>
        <BecomeSellerCallout
          title="Ative a venda na sua conta"
          description="Para anunciar, sua conta precisa do perfil de vendedor. Você continua comprando normalmente."
        />
      </section>
    )
  }

  const token = state.token

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFieldErrors({})
    setFormError(null)

    const parsedPrice = parsePrice(price)
    if (parsedPrice === null) {
      setFieldErrors({ price: 'Informe um preço válido, por exemplo 199,90.' })
      return
    }

    setSubmitting(true)
    try {
      const product = await productsApi.create(
        { name, description: description.trim() || undefined, price: parsedPrice, stock: Number(stock) || 0 },
        token,
      )
      navigate(`/produtos/${product.id}`, { state: { published: true } })
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        setFieldErrors(translateFieldErrors(error.fieldErrors, 'product'))
      } else if (error instanceof ApiError && error.status === 403) {
        setFormError('Sua conta ainda não pode vender. Recarregue a página e tente de novo.')
      } else {
        setFormError('Não foi possível publicar agora. Tente novamente.')
      }
      setSubmitting(false)
    }
  }

  return (
    <section className="panel">
      <h1>Anunciar um produto</h1>
      <p className="muted">O produto aparece na vitrine assim que for publicado.</p>

      {formError ? (
        <div className="alert alert--error" role="alert">
          {formError}
        </div>
      ) : null}

      <form onSubmit={handleSubmit} className="form">
        <FormField
          label="Nome do produto"
          name="name"
          required
          maxLength={120}
          value={name}
          onChange={(e) => setName(e.target.value)}
          error={fieldErrors.name}
        />
        <TextAreaField
          label="Descrição"
          name="description"
          rows={4}
          maxLength={2000}
          hint="Opcional. Conte o que vem na caixa, o estado e os detalhes importantes."
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          error={fieldErrors.description}
        />
        <div className="form__row">
          <FormField
            label="Preço (R$)"
            name="price"
            inputMode="decimal"
            placeholder="199,90"
            required
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            error={fieldErrors.price ?? fieldErrors.currency}
          />
          <FormField
            label="Estoque"
            name="stock"
            type="number"
            min={0}
            step={1}
            required
            value={stock}
            onChange={(e) => setStock(e.target.value)}
            error={fieldErrors.stock}
          />
        </div>
        <button type="submit" className="button button--primary button--block" disabled={submitting}>
          {submitting ? 'Publicando…' : 'Publicar produto'}
        </button>
      </form>
    </section>
  )
}
