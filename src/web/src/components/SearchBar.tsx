import { useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { Icon } from './Icon'

/** Busca do cabeçalho: leva para a vitrine com `?busca=`, mantendo a categoria escolhida. */
export function SearchBar() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const current = searchParams.get('busca') ?? ''
  // Texto digitado; o da URL só entra quando a URL muda (navegar por link, voltar no histórico).
  const [draft, setDraft] = useState({ source: current, text: current })
  const text = draft.source === current ? draft.text : current

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const params = new URLSearchParams()
    const term = text.trim()
    if (term) {
      params.set('busca', term)
    }
    const category = searchParams.get('categoria')
    if (category) {
      params.set('categoria', category)
    }
    const query = params.toString()
    navigate(query ? `/?${query}` : '/')
  }

  return (
    <form className="search" role="search" onSubmit={handleSubmit}>
      <input
        type="search"
        className="search__input"
        placeholder="Buscar produtos, marcas e muito mais…"
        aria-label="Buscar produtos"
        maxLength={100}
        value={text}
        onChange={(e) => setDraft({ source: current, text: e.target.value })}
      />
      <button type="submit" className="search__button" aria-label="Buscar">
        <Icon name="search" />
      </button>
    </form>
  )
}
