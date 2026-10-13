export interface Session {
  accessToken: string
  expiresAt: string
}

const STORAGE_KEY = 'marketplace.session'

export function saveSession(session: Session): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  } catch {
    // Storage bloqueado (modo privado etc.): a sessão vale só até recarregar a página.
  }
}

/** Sessão salva e ainda válida; expirada ou corrompida é apagada e retorna null. */
export function loadSession(): Session | null {
  let raw: string | null
  try {
    raw = localStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
  if (!raw) {
    return null
  }

  try {
    const session = JSON.parse(raw) as Partial<Session>
    const expiresAt = Date.parse(session.expiresAt ?? '')
    if (typeof session.accessToken === 'string' && expiresAt > Date.now()) {
      return { accessToken: session.accessToken, expiresAt: session.expiresAt! }
    }
  } catch {
    // Conteúdo inválido: cai no clear abaixo.
  }

  clearSession()
  return null
}

export function clearSession(): void {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    // Nada a fazer.
  }
}
