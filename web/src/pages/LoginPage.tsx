import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthContext'

/** Demo-only accounts with fake data; the password is shown so reviewers can try the app. */
const DEMO_PASSWORD = 'Demo#Auction2026'
const DEMO_ACCOUNTS = [
  { email: 'bidder@demo.test', label: 'Mali (bidder)' },
  { email: 'bidder2@demo.test', label: 'Tun (bidder)' },
  { email: 'admin@demo.test', label: 'Admin' },
]

export function LoginPage() {
  const { login, register } = useAuth()
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/'
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      navigate(from, { replace: true })
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    void run(() => mode === 'login' ? login(email, password) : register(email, name, password))
  }

  return (
    <div className="auth-wrap">
      <form className="card auth" onSubmit={onSubmit}>
        <h1>{mode === 'login' ? 'Sign in' : 'Create a bidder account'}</h1>
        {mode === 'register' && (
          <label>Display name<input value={name} onChange={e => setName(e.target.value)} required maxLength={60} /></label>
        )}
        <label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} required autoComplete="username" /></label>
        <label>Password
          <input type="password" value={password} onChange={e => setPassword(e.target.value)} required
                 autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={mode === 'register' ? 10 : undefined} />
        </label>
        {error && <div className="error">{error}</div>}
        <button className="btn wide" disabled={busy}>{busy ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account'}</button>
        <button type="button" className="link" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
          {mode === 'login' ? 'New here? Create an account' : 'Have an account? Sign in'}
        </button>

        <div className="demo">
          <p className="muted small">Or try a demo account:</p>
          <div className="demo-buttons">
            {DEMO_ACCOUNTS.map(a => (
              <button key={a.email} type="button" className="btn btn-ghost" disabled={busy}
                      onClick={() => void run(() => login(a.email, DEMO_PASSWORD))}>
                {a.label}
              </button>
            ))}
          </div>
          <p className="muted small">Tip: open a second browser window as the other bidder and bid against yourself.</p>
        </div>
      </form>
    </div>
  )
}
