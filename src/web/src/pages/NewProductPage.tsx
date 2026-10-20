import { useState, type FormEvent, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, type FieldErrors } from '../api/client'
import { translateFieldErrors } from '../api/messages'
import { productsApi, type NewProduct, type Product, type ProductCondition } from '../api/products'
import { isSeller } from '../api/users'
import { useAuth } from '../auth/context'
import { CATEGORIES, CONDITIONS } from '../catalog/categories'
import { BecomeSellerCallout } from '../components/BecomeSellerCallout'
import { FormField, TextAreaField } from '../components/FormField'
import { Icon } from '../components/Icon'
import { ProductCard } from '../components/ProductCard'
import { parsePrice } from '../utils/money'

const MAX_IMAGES = 8
const MAX_ATTRIBUTES = 20
/** A pré-visualização não mostra data; o valor só completa o tipo. */
const PREVIEW_CREATED_AT = new Date(0).toISOString()

interface Attribute {
  key: number
  name: string
  value: string
}

let nextKey = 1

/** Inteiro opcional digitado: vazio → undefined; inválido → NaN (vira erro no campo). */
function optionalInt(text: string): number | undefined {
  const trimmed = text.trim()
  if (!trimmed) {
    return undefined
  }
  return /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN
}

const isHttpUrl = (text: string) => {
  try {
    const url = new URL(text)
    return url.protocol === 'https:' || url.protocol === 'http:'
  } catch {
    return false
  }
}

export function NewProductPage() {
  const { state } = useAuth()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [category, setCategory] = useState('')
  const [brand, setBrand] = useState('')
  const [sku, setSku] = useState('')
  const [condition, setCondition] = useState<ProductCondition>('New')
  const [imageUrls, setImageUrls] = useState<string[]>([''])
  const [description, setDescription] = useState('')
  const [attributes, setAttributes] = useState<Attribute[]>([])
  const [price, setPrice] = useState('')
  const [originalPrice, setOriginalPrice] = useState('')
  const [stock, setStock] = useState('1')
  const [weight, setWeight] = useState('')
  const [length, setLength] = useState('')
  const [width, setWidth] = useState('')
  const [height, setHeight] = useState('')
  const [warranty, setWarranty] = useState('')
  const [freeShipping, setFreeShipping] = useState(false)
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
  const filledImages = imageUrls.map((url) => url.trim()).filter(Boolean)

  /** Valida no navegador o que dá para validar e monta o corpo; erros vão para os campos. */
  function buildProduct(): { product: NewProduct } | { errors: FieldErrors } {
    const errors: FieldErrors = {}
    const parsedPrice = parsePrice(price)
    if (parsedPrice === null) {
      errors.price = 'Informe um preço válido, por exemplo 199,90.'
    }
    const parsedOriginal = originalPrice.trim() ? parsePrice(originalPrice) : undefined
    if (parsedOriginal === null) {
      errors.originalPrice = 'Informe um preço válido, por exemplo 249,90.'
    } else if (parsedOriginal !== undefined && parsedPrice !== null && parsedOriginal <= parsedPrice) {
      errors.originalPrice = 'O preço original precisa ser maior que o preço de venda.'
    }
    if (filledImages.some((url) => !isHttpUrl(url))) {
      errors.imageUrls = 'Use endereços completos de imagem, começando com https://.'
    }
    const measures = { weightGrams: weight, lengthCm: length, widthCm: width, heightCm: height, warrantyMonths: warranty }
    const parsedMeasures: Record<string, number | undefined> = {}
    for (const [field, text] of Object.entries(measures)) {
      const value = optionalInt(text)
      if (Number.isNaN(value)) {
        errors[field] = 'Use apenas números inteiros.'
      }
      parsedMeasures[field] = value
    }
    const filledAttributes = attributes
      .map((a) => ({ name: a.name.trim(), value: a.value.trim() }))
      .filter((a) => a.name || a.value)
    if (filledAttributes.some((a) => !a.name || !a.value)) {
      errors.attributes = 'Preencha a característica e o valor de cada linha (ou remova a linha).'
    }

    if (Object.keys(errors).length > 0 || parsedPrice === null) {
      return { errors }
    }

    const product: NewProduct = {
      name,
      description: description.trim() || undefined,
      price: parsedPrice,
      stock: Number(stock) || 0,
      condition,
    }
    if (category) product.category = category
    if (brand.trim()) product.brand = brand.trim()
    if (sku.trim()) product.sku = sku.trim()
    if (filledImages.length > 0) product.imageUrls = filledImages
    if (parsedOriginal != null) product.originalPrice = parsedOriginal
    for (const [field, value] of Object.entries(parsedMeasures)) {
      if (value !== undefined) {
        ;(product as unknown as Record<string, number>)[field] = value
      }
    }
    if (freeShipping) product.freeShipping = true
    if (filledAttributes.length > 0) product.attributes = filledAttributes
    return { product }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFieldErrors({})
    setFormError(null)

    const built = buildProduct()
    if ('errors' in built) {
      setFieldErrors(built.errors)
      return
    }

    setSubmitting(true)
    try {
      const product = await productsApi.create(built.product, token)
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

  const previewProduct: Product = {
    id: 'preview',
    sellerId: state.user.id,
    name: name.trim() || 'Seu produto aqui',
    description: null,
    price: parsePrice(price) ?? 0,
    currency: 'BRL',
    stock: Number(stock) || 0,
    createdAt: PREVIEW_CREATED_AT,
    brand: brand.trim() || null,
    condition,
    imageUrls: filledImages.filter(isHttpUrl),
    originalPrice: originalPrice.trim() ? parsePrice(originalPrice) : null,
    freeShipping,
  }

  return (
    <div className="listing">
      <header className="listing__header">
        <h1>Anunciar um produto</h1>
        <p className="muted">Quanto mais completo o anúncio, mais fácil para o comprador decidir. O produto aparece na vitrine assim que for publicado.</p>
      </header>

      {formError ? (
        <div className="alert alert--error" role="alert">
          {formError}
        </div>
      ) : null}

      <div className="listing__layout">
        <form onSubmit={handleSubmit} className="listing__form">
          <FormSection number={1} title="Informações principais" description="Como o produto aparece na busca e na vitrine.">
            <FormField
              label="Nome do produto"
              name="name"
              required
              maxLength={120}
              placeholder="Ex.: Furadeira de impacto 700W com maleta"
              value={name}
              onChange={(e) => setName(e.target.value)}
              error={fieldErrors.name}
            />
            <div className="form__grid">
              <SelectField
                label="Categoria"
                value={category}
                onChange={setCategory}
                error={fieldErrors.category}
                hint="Sem categoria, o produto vai para “Outros”."
              >
                <option value="">Selecione…</option>
                {CATEGORIES.map((c) => (
                  <option key={c.code} value={c.code}>
                    {c.label}
                  </option>
                ))}
              </SelectField>
              <FormField
                label="Marca"
                name="brand"
                maxLength={60}
                placeholder="Ex.: Bosch"
                value={brand}
                onChange={(e) => setBrand(e.target.value)}
                error={fieldErrors.brand}
              />
            </div>
            <div className="form__grid">
              <fieldset className="field segmented">
                <legend className="field__label">Condição</legend>
                <div className="segmented__options">
                  {CONDITIONS.map((c) => (
                    <label key={c.value} className={`segmented__option${condition === c.value ? ' segmented__option--active' : ''}`}>
                      <input type="radio" name="condition" value={c.value} checked={condition === c.value} onChange={() => setCondition(c.value)} />
                      {c.label}
                    </label>
                  ))}
                </div>
                {fieldErrors.condition ? <p className="field__error">{fieldErrors.condition}</p> : null}
              </fieldset>
              <FormField
                label="Código do produto (SKU)"
                name="sku"
                maxLength={40}
                hint="Opcional. Seu código interno de controle."
                value={sku}
                onChange={(e) => setSku(e.target.value)}
                error={fieldErrors.sku}
              />
            </div>
          </FormSection>

          <FormSection number={2} title="Fotos" description={`Até ${MAX_IMAGES} fotos. A primeira é a capa do anúncio.`}>
            <ul className="image-inputs">
              {imageUrls.map((url, index) => (
                <li key={index} className="image-inputs__row">
                  <div className="image-inputs__thumb" aria-hidden="true">
                    {isHttpUrl(url.trim()) ? <img src={url.trim()} alt="" /> : <Icon name="image" size={22} />}
                  </div>
                  <input
                    className="field__input"
                    inputMode="url"
                    placeholder="https://…/foto.jpg"
                    aria-label={`Endereço da foto ${index + 1}`}
                    aria-invalid={fieldErrors.imageUrls && url.trim() && !isHttpUrl(url.trim()) ? true : undefined}
                    maxLength={500}
                    value={url}
                    onChange={(e) => setImageUrls(imageUrls.map((u, i) => (i === index ? e.target.value : u)))}
                  />
                  {imageUrls.length > 1 ? (
                    <button
                      type="button"
                      className="icon-button"
                      aria-label={`Remover foto ${index + 1}`}
                      onClick={() => setImageUrls(imageUrls.filter((_, i) => i !== index))}
                    >
                      <Icon name="close" size={18} />
                    </button>
                  ) : null}
                </li>
              ))}
            </ul>
            {fieldErrors.imageUrls ? <p className="field__error">{fieldErrors.imageUrls}</p> : null}
            {imageUrls.length < MAX_IMAGES ? (
              <button type="button" className="button button--ghost button--small" onClick={() => setImageUrls([...imageUrls, ''])}>
                <Icon name="plus" size={16} /> Adicionar foto
              </button>
            ) : null}
          </FormSection>

          <FormSection number={3} title="Descrição e ficha técnica" description="Conte o que vem na caixa, o estado e os detalhes importantes.">
            <TextAreaField
              label="Descrição"
              name="description"
              rows={6}
              maxLength={2000}
              hint={`Opcional. ${description.length}/2000 caracteres.`}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              error={fieldErrors.description}
            />
            <div className="field">
              <span className="field__label">Características</span>
              <p className="field__hint">Ex.: Voltagem = 220V, Cor = Azul, Material = Aço inox.</p>
              {attributes.length > 0 ? (
                <ul className="attribute-inputs">
                  {attributes.map((attribute, index) => (
                    <li key={attribute.key} className="attribute-inputs__row">
                      <input
                        className="field__input"
                        placeholder="Característica"
                        aria-label={`Característica ${index + 1}`}
                        maxLength={40}
                        value={attribute.name}
                        onChange={(e) =>
                          setAttributes(attributes.map((a) => (a.key === attribute.key ? { ...a, name: e.target.value } : a)))
                        }
                      />
                      <input
                        className="field__input"
                        placeholder="Valor"
                        aria-label={`Valor ${index + 1}`}
                        maxLength={120}
                        value={attribute.value}
                        onChange={(e) =>
                          setAttributes(attributes.map((a) => (a.key === attribute.key ? { ...a, value: e.target.value } : a)))
                        }
                      />
                      <button
                        type="button"
                        className="icon-button"
                        aria-label={`Remover característica ${index + 1}`}
                        onClick={() => setAttributes(attributes.filter((a) => a.key !== attribute.key))}
                      >
                        <Icon name="close" size={18} />
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
              {fieldErrors.attributes ? <p className="field__error">{fieldErrors.attributes}</p> : null}
              {attributes.length < MAX_ATTRIBUTES ? (
                <button
                  type="button"
                  className="button button--ghost button--small attribute-inputs__add"
                  onClick={() => setAttributes([...attributes, { key: nextKey++, name: '', value: '' }])}
                >
                  <Icon name="plus" size={16} /> Adicionar característica
                </button>
              ) : null}
            </div>
          </FormSection>

          <FormSection number={4} title="Preço e disponibilidade">
            <div className="form__grid form__grid--3">
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
                label="Preço original (R$)"
                name="originalPrice"
                inputMode="decimal"
                placeholder="249,90"
                hint="Opcional. Mostra o desconto no anúncio."
                value={originalPrice}
                onChange={(e) => setOriginalPrice(e.target.value)}
                error={fieldErrors.originalPrice}
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
          </FormSection>

          <FormSection number={5} title="Envio e garantia" description="Medidas da embalagem, para o comprador saber o que vai receber.">
            <div className="form__grid form__grid--4">
              <FormField label="Peso (g)" name="weightGrams" inputMode="numeric" value={weight} onChange={(e) => setWeight(e.target.value)} error={fieldErrors.weightGrams} />
              <FormField label="Comprimento (cm)" name="lengthCm" inputMode="numeric" value={length} onChange={(e) => setLength(e.target.value)} error={fieldErrors.lengthCm} />
              <FormField label="Largura (cm)" name="widthCm" inputMode="numeric" value={width} onChange={(e) => setWidth(e.target.value)} error={fieldErrors.widthCm} />
              <FormField label="Altura (cm)" name="heightCm" inputMode="numeric" value={height} onChange={(e) => setHeight(e.target.value)} error={fieldErrors.heightCm} />
            </div>
            <div className="form__grid">
              <FormField
                label="Garantia (meses)"
                name="warrantyMonths"
                inputMode="numeric"
                hint="Opcional. Garantia oferecida por você."
                value={warranty}
                onChange={(e) => setWarranty(e.target.value)}
                error={fieldErrors.warrantyMonths}
              />
              <label className="switch">
                <input type="checkbox" checked={freeShipping} onChange={(e) => setFreeShipping(e.target.checked)} />
                <span className="switch__text">
                  <strong>Oferecer frete grátis</strong>
                  <span className="muted">O anúncio ganha o selo e aparece no filtro de frete grátis.</span>
                </span>
              </label>
            </div>
          </FormSection>

          <div className="listing__submit">
            <button type="submit" className="button button--primary button--large" disabled={submitting}>
              {submitting ? 'Publicando…' : 'Publicar produto'}
            </button>
          </div>
        </form>

        <aside className="listing__aside" aria-label="Pré-visualização">
          <p className="listing__aside-title">Pré-visualização</p>
          <ProductCard product={previewProduct} preview />
          <div className="tips">
            <p className="tips__title">Dicas para vender mais</p>
            <ul>
              <li>Use fotos claras, com fundo neutro.</li>
              <li>Coloque marca e modelo no nome.</li>
              <li>Preencha a ficha técnica: voltagem, tamanho, cor.</li>
              <li>Ofereça frete grátis para destacar o anúncio.</li>
            </ul>
          </div>
        </aside>
      </div>
    </div>
  )
}

function FormSection({ number, title, description, children }: { number: number; title: string; description?: string; children: ReactNode }) {
  const id = `section-${number}`
  return (
    <section className="form-section" aria-labelledby={id}>
      <div className="form-section__header">
        <div>
          <h2 id={id}>{title}</h2>
          {description ? <p className="muted">{description}</p> : null}
        </div>
      </div>
      <div className="form-section__body">{children}</div>
    </section>
  )
}

function SelectField({
  label,
  value,
  onChange,
  error,
  hint,
  children,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  error?: string
  hint?: string
  children: ReactNode
}) {
  const id = `select-${label.toLowerCase().replace(/\W+/g, '-')}`
  return (
    <div className="field">
      <label htmlFor={id} className="field__label">
        {label}
      </label>
      <select
        id={id}
        className="field__input"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : hint ? `${id}-hint` : undefined}
      >
        {children}
      </select>
      {error ? (
        <p id={`${id}-error`} className="field__error">
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className="field__hint">
          {hint}
        </p>
      ) : null}
    </div>
  )
}
