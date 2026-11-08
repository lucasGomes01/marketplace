import { useEffect, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { productsApi, type Page, type Product, type ProductCondition, type ProductFilter, type ProductSort } from '../api/products'
import { useAuth } from '../auth/context'
import { CATEGORIES, CONDITIONS, categoryLabel } from '../catalog/categories'
import { Icon } from '../components/Icon'
import { ProductCard } from '../components/ProductCard'
import { parsePrice } from '../utils/money'

export const PAGE_SIZE = 12

type Load = { status: 'loading' } | { status: 'error' } | { status: 'ready'; page: Page<Product> }

const SORTS: { value: ProductSort; label: string }[] = [
  { value: 'newest', label: 'Mais recentes' },
  { value: 'price_asc', label: 'Menor preço' },
  { value: 'price_desc', label: 'Maior preço' },
  { value: 'name', label: 'Nome (A–Z)' },
]

/** Filtros da vitrine lidos da URL (em português, para o endereço ser legível e compartilhável). */
function readFilter(params: URLSearchParams): ProductFilter {
  const condition = params.get('condicao')
  const sort = params.get('ordem')
  const min = parsePrice(params.get('min') ?? '')
  const max = parsePrice(params.get('max') ?? '')
  return {
    search: params.get('busca')?.trim() || undefined,
    category: CATEGORIES.some((c) => c.code === params.get('categoria')) ? params.get('categoria')! : undefined,
    condition: CONDITIONS.some((c) => c.value === condition) ? (condition as ProductCondition) : undefined,
    minPrice: min ?? undefined,
    maxPrice: max ?? undefined,
    freeShipping: params.get('frete') === '1' || undefined,
    sort: SORTS.some((s) => s.value === sort) ? (sort as ProductSort) : undefined,
  }
}

const hasFilters = (filter: ProductFilter) =>
  Boolean(filter.search || filter.category || filter.condition || filter.minPrice !== undefined || filter.maxPrice !== undefined || filter.freeShipping)

export function HomePage() {
  const { state } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const page = Math.max(1, Number(searchParams.get('pagina')) || 1)
  const filter = readFilter(searchParams)
  const filtered = hasFilters(filter)
  // Chave da consulta: o resultado guardado só vale para ela (sem setState síncrono no efeito).
  const key = JSON.stringify({ page, filter })
  const [result, setResult] = useState<{ key: string; load: Load } | null>(null)
  const load: Load = result?.key === key ? result.load : { status: 'loading' }

  useEffect(() => {
    const controller = new AbortController()
    const { page: requested, filter: requestedFilter } = JSON.parse(key) as { page: number; filter: ProductFilter }
    productsApi
      .list(requested, PAGE_SIZE, controller.signal, requestedFilter)
      .then((data) => setResult({ key, load: { status: 'ready', page: data } }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setResult({ key, load: { status: 'error' } })
        }
      })
    return () => controller.abort()
  }, [key])

  /** Troca parâmetros da URL e volta para a primeira página. */
  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(searchParams)
    for (const [name, value] of Object.entries(changes)) {
      if (value) {
        next.set(name, value)
      } else {
        next.delete(name)
      }
    }
    next.delete('pagina')
    setSearchParams(next)
  }

  const title = filter.search
    ? `Resultados para “${filter.search}”`
    : (categoryLabel(filter.category) ?? (filter.freeShipping ? 'Frete grátis' : 'Produtos'))
  const landing = !filtered && page === 1

  return (
    <>
      {landing ? (
        <>
          <div className="home-top">
            <section className="welcome">
              <p className="welcome__greeting">
                {state.status === 'authenticated' ? `Olá, ${state.user.name.split(' ')[0]}.` : 'Vários vendedores, um carrinho só.'}
              </p>
              <h1>Compre e venda direto de quem faz.</h1>
              <p className="welcome__lead">
                Produtos novos, usados e recondicionados de vendedores independentes. O estoque é reservado quando o
                pedido confirma e cada envio pode ser acompanhado até a entrega.
              </p>
              <a href="#catalogo" className="button button--primary">
                Ver produtos
              </a>
            </section>

            <aside className="sell-box" aria-labelledby="sell-title">
              <h2 id="sell-title">Venda no Marketplace</h2>
              <ul>
                <li>Anúncio com fotos, ficha técnica e preço promocional</li>
                <li>Pedidos a despachar organizados por etapa</li>
                <li>Mesma conta para comprar e vender</li>
              </ul>
              <Link to="/vender/novo" className="button">
                Começar a anunciar
              </Link>
            </aside>
          </div>

          <section className="departments" aria-labelledby="departments-title">
            <h2 id="departments-title" className="section-title">
              Departamentos
            </h2>
            <ul className="departments__list">
              {CATEGORIES.map((category) => (
                <li key={category.code}>
                  <Link to={`/?categoria=${category.code}`}>
                    <span className="departments__name">{category.label}</span>
                    <span className="departments__blurb">{category.blurb}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        </>
      ) : (
        <nav className="breadcrumb" aria-label="Você está em">
          <Link to="/">Início</Link>
          <Icon name="chevronRight" size={14} />
          <span aria-current="page">{title}</span>
        </nav>
      )}

      <section aria-labelledby="catalog-title" className="catalog" id="catalogo">
        <div className="catalog__header">
          {landing ? (
            <h2 id="catalog-title" className="section-title">
              Produtos
            </h2>
          ) : (
            <h1 id="catalog-title" className="catalog__heading">
              {title}
            </h1>
          )}
          <div className="catalog__tools">
            {load.status === 'ready' ? (
              <span className="muted catalog__count">
                {load.page.totalCount === 1 ? '1 produto' : `${load.page.totalCount} produtos`}
              </span>
            ) : null}
            <label className="sort">
              <span>Ordenar por</span>
              <select
                className="field__input sort__select"
                value={filter.sort ?? 'newest'}
                onChange={(e) => update({ ordem: e.target.value === 'newest' ? null : e.target.value })}
              >
                {SORTS.map((sort) => (
                  <option key={sort.value} value={sort.value}>
                    {sort.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </div>

        <div className="catalog__layout">
          <Filters filter={filter} filtered={filtered} update={update} />
          <div className="catalog__results">
            <Catalog load={load} page={page} filtered={filtered} searchParams={searchParams} />
          </div>
        </div>
      </section>
    </>
  )
}

function Filters({
  filter,
  filtered,
  update,
}: {
  filter: ProductFilter
  filtered: boolean
  update: (changes: Record<string, string | null>) => void
}) {
  const [open, setOpen] = useState(false)
  const priceKey = `${filter.minPrice ?? ''}-${filter.maxPrice ?? ''}`

  function applyPrice(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    const min = parsePrice(String(data.get('min') ?? ''))
    const max = parsePrice(String(data.get('max') ?? ''))
    update({ min: min === null ? null : String(min), max: max === null ? null : String(max) })
  }

  return (
    <aside className={`filters${open ? ' filters--open' : ''}`} aria-label="Filtros">
      <button type="button" className="button button--ghost filters__toggle" aria-expanded={open} onClick={() => setOpen(!open)}>
        <Icon name="filter" size={18} /> Filtros
      </button>

      <div className="filters__body">
        <fieldset className="filters__group">
          <legend>Categoria</legend>
          <ul className="filters__list">
            <li>
              <button
                type="button"
                className={`filters__option${filter.category ? '' : ' filters__option--active'}`}
                aria-pressed={!filter.category}
                onClick={() => update({ categoria: null })}
              >
                Todas
              </button>
            </li>
            {CATEGORIES.map((category) => (
              <li key={category.code}>
                <button
                  type="button"
                  className={`filters__option${filter.category === category.code ? ' filters__option--active' : ''}`}
                  aria-pressed={filter.category === category.code}
                  onClick={() => update({ categoria: category.code })}
                >
                  {category.label}
                </button>
              </li>
            ))}
          </ul>
        </fieldset>

        <fieldset className="filters__group">
          <legend>Condição</legend>
          {[{ value: '', label: 'Qualquer' }, ...CONDITIONS].map((condition) => (
            <label key={condition.value} className="filters__check">
              <input
                type="radio"
                name="condicao"
                checked={(filter.condition ?? '') === condition.value}
                onChange={() => update({ condicao: condition.value || null })}
              />
              {condition.label}
            </label>
          ))}
        </fieldset>

        <fieldset className="filters__group">
          <legend>Preço (R$)</legend>
          <form className="filters__price" onSubmit={applyPrice} key={priceKey}>
            <input
              name="min"
              className="field__input"
              inputMode="decimal"
              placeholder="Mínimo"
              aria-label="Preço mínimo"
              defaultValue={filter.minPrice ?? ''}
            />
            <span aria-hidden="true">–</span>
            <input
              name="max"
              className="field__input"
              inputMode="decimal"
              placeholder="Máximo"
              aria-label="Preço máximo"
              defaultValue={filter.maxPrice ?? ''}
            />
            <button type="submit" className="button button--ghost button--small">
              Aplicar
            </button>
          </form>
        </fieldset>

        <fieldset className="filters__group">
          <legend>Entrega</legend>
          <label className="filters__check">
            <input type="checkbox" checked={filter.freeShipping ?? false} onChange={(e) => update({ frete: e.target.checked ? '1' : null })} />
            Somente frete grátis
          </label>
        </fieldset>

        {filtered ? (
          <button
            type="button"
            className="button button--ghost button--block"
            onClick={() => update({ busca: null, categoria: null, condicao: null, min: null, max: null, frete: null })}
          >
            Limpar filtros
          </button>
        ) : null}
      </div>
    </aside>
  )
}

function Catalog({
  load,
  page,
  filtered,
  searchParams,
}: {
  load: Load
  page: number
  filtered: boolean
  searchParams: URLSearchParams
}) {
  if (load.status === 'loading') {
    return (
      <div className="product-grid" aria-busy="true" aria-label="Carregando produtos…">
        {Array.from({ length: 8 }, (_, i) => (
          <div key={i} className="product-card product-card--skeleton" aria-hidden="true" />
        ))}
      </div>
    )
  }
  if (load.status === 'error') {
    return (
      <div className="alert alert--error" role="alert">
        Não foi possível carregar os produtos. Tente recarregar a página.
      </div>
    )
  }

  const { items, totalCount } = load.page
  if (totalCount === 0) {
    return filtered ? (
      <div className="empty-state">
        <Icon name="search" size={32} />
        <p>Nenhum produto encontrado com esses filtros.</p>
        <Link to="/" className="button button--ghost">
          Ver todos os produtos
        </Link>
      </div>
    ) : (
      <div className="empty-state">
        <Icon name="box" size={32} />
        <p>Nenhum produto à venda ainda.</p>
        <Link to="/vender/novo" className="button button--primary">
          Anunciar um produto
        </Link>
      </div>
    )
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const pageLink = (target: number) => {
    const params = new URLSearchParams(searchParams)
    if (target === 1) {
      params.delete('pagina')
    } else {
      params.set('pagina', String(target))
    }
    const query = params.toString()
    return query ? `/?${query}` : '/'
  }

  return (
    <>
      <div className="product-grid">
        {items.map((product) => (
          <ProductCard key={product.id} product={product} />
        ))}
      </div>
      {totalPages > 1 ? (
        <nav className="pagination" aria-label="Paginação">
          {page > 1 ? (
            <Link to={pageLink(page - 1)} className="button button--ghost">
              Anterior
            </Link>
          ) : (
            <span />
          )}
          <span className="muted">
            Página {page} de {totalPages}
          </span>
          {page < totalPages ? (
            <Link to={pageLink(page + 1)} className="button button--ghost">
              Próxima
            </Link>
          ) : (
            <span />
          )}
        </nav>
      ) : null}
    </>
  )
}
