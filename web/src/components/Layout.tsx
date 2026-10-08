import { Link, NavLink, Outlet } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { useRealtime } from '../realtime/RealtimeContext'

export function Layout() {
  const { user, logout } = useAuth()
  const { state, toasts, dismiss } = useRealtime()

  return (
    <>
      <header className="topbar">
        <Link to="/" className="brand"><span className="brand-mark">▣</span> BoxBid</Link>
        <nav>
          <NavLink to="/" end>Auctions</NavLink>
          {user && <NavLink to="/my-bids">My bids</NavLink>}
          {user?.role === 'Admin' && <NavLink to="/admin/new">New auction</NavLink>}
        </nav>
        <div className="right">
          <span className={`live live-${state}`} title={`Realtime connection: ${state}`}>
            <span className="dot" />{state === 'live' ? 'Live' : state}
          </span>
          {user ? (
            <>
              <span className="who">{user.displayName}</span>
              <button className="btn btn-ghost" onClick={logout}>Sign out</button>
            </>
          ) : (
            <Link className="btn" to="/login">Sign in to bid</Link>
          )}
        </div>
      </header>

      <main className="page">
        <Outlet />
      </main>

      <footer className="footer muted small">
        Demo with fake data. Simulated bidders keep auctions moving · <a href="/docs">API docs</a>
      </footer>

      <div className="toasts" aria-live="polite">
        {toasts.map(t => (
          <div key={t.id} className={`toast toast-${t.tone}`}>
            <div>
              <strong>{t.title}</strong>
              <p>{t.body}</p>
              {t.auctionId != null && <Link to={`/auctions/${t.auctionId}`} onClick={() => dismiss(t.id)}>Open auction →</Link>}
            </div>
            <button className="close" aria-label="Dismiss" onClick={() => dismiss(t.id)}>×</button>
          </div>
        ))}
      </div>
    </>
  )
}
