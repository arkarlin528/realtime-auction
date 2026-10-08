import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { api } from './api/client'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { Layout } from './components/Layout'
import { syncServerClock } from './lib/clock'
import { AuctionRoomPage } from './pages/AuctionRoomPage'
import { LobbyPage } from './pages/LobbyPage'
import { LoginPage } from './pages/LoginPage'
import { MyBidsPage } from './pages/MyBidsPage'
import { NewAuctionPage } from './pages/NewAuctionPage'
import { RealtimeProvider } from './realtime/RealtimeContext'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    // SignalR keeps the cache fresh, so background refetching is only a safety net.
    queries: { staleTime: 30_000, refetchOnWindowFocus: true, retry: 1 },
  },
})

// Line our clock up with the server's before the first countdown renders.
const started = Date.now()
void api.time().then(t => syncServerClock(t.now, started, Date.now())).catch(() => undefined)

function RequireUser({ children, admin = false }: { children: ReactNode; admin?: boolean }) {
  const { user } = useAuth()
  if (!user) return <Navigate to="/login" replace />
  if (admin && user.role !== 'Admin') return <Navigate to="/" replace />
  return children
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RealtimeProvider>
          <BrowserRouter>
            <Routes>
              <Route element={<Layout />}>
                <Route index element={<LobbyPage />} />
                <Route path="auctions/:id" element={<AuctionRoomPage />} />
                <Route path="login" element={<LoginPage />} />
                <Route path="my-bids" element={<RequireUser><MyBidsPage /></RequireUser>} />
                <Route path="admin/new" element={<RequireUser admin><NewAuctionPage /></RequireUser>} />
                <Route path="*" element={<Navigate to="/" replace />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </RealtimeProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
