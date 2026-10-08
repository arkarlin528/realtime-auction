import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { api, configureAuth } from '../api/client'
import type { LoginResponse, User } from '../api/types'

const STORAGE_KEY = 'auction.session'

interface Session {
  token: string
  expiresAt: string
  user: User
}

interface AuthValue {
  user: User | null
  token: string | null
  login: (email: string, password: string) => Promise<void>
  register: (email: string, displayName: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthValue | null>(null)

function restore(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const session = JSON.parse(raw) as Session
    return new Date(session.expiresAt) > new Date() ? session : null
  } catch {
    return null
  }
}

function persist(session: Session | null) {
  try {
    if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    // storage unavailable (private mode): the session still works for this tab
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(restore)

  const logout = useCallback(() => {
    persist(null)
    setSession(null)
  }, [])

  // Keep the API client in sync during render, so the very first query already carries the token.
  configureAuth(session?.token ?? null, logout)

  const start = useCallback((response: LoginResponse) => {
    const next = { token: response.accessToken, expiresAt: response.expiresAt, user: response.user }
    persist(next)
    setSession(next)
  }, [])

  const value = useMemo<AuthValue>(() => ({
    user: session?.user ?? null,
    token: session?.token ?? null,
    login: async (email, password) => start(await api.login(email, password)),
    register: async (email, displayName, password) => start(await api.register(email, displayName, password)),
    logout,
  }), [session, start, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthValue {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>')
  return value
}
