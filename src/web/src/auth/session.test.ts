import { describe, expect, it } from 'vitest'
import { clearSession, loadSession, saveSession } from './session'

const inOneHour = () => new Date(Date.now() + 60 * 60 * 1000).toISOString()

describe('session storage', () => {
  it('returns null when nothing is stored', () => {
    expect(loadSession()).toBeNull()
  })

  it('loads a saved session', () => {
    const session = { accessToken: 'token', expiresAt: inOneHour() }

    saveSession(session)

    expect(loadSession()).toEqual(session)
  })

  it('discards an expired session', () => {
    saveSession({ accessToken: 'old', expiresAt: new Date(Date.now() - 1000).toISOString() })

    expect(loadSession()).toBeNull()
    expect(localStorage.length).toBe(0)
  })

  it('discards corrupted data', () => {
    localStorage.setItem('marketplace.session', '{not json')

    expect(loadSession()).toBeNull()
  })

  it('clears the session', () => {
    saveSession({ accessToken: 'token', expiresAt: inOneHour() })

    clearSession()

    expect(loadSession()).toBeNull()
  })
})
