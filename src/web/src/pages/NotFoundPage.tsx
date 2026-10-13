import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <section className="panel">
      <h1>Página não encontrada</h1>
      <p className="muted">O endereço não existe ou mudou.</p>
      <Link to="/">Voltar para o início</Link>
    </section>
  )
}
