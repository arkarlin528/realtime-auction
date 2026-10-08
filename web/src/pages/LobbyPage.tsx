import { useState } from 'react'
import { useAuctions } from '../api/hooks'
import type { AuctionListFilter } from '../api/types'
import { AuctionCard } from '../components/AuctionCard'
import { useGroup } from '../realtime/RealtimeContext'

const TABS: { filter: AuctionListFilter; label: string }[] = [
  { filter: 'Live', label: 'Live now' },
  { filter: 'Scheduled', label: 'Upcoming' },
  { filter: 'Closed', label: 'Results' },
]

export function LobbyPage() {
  const [filter, setFilter] = useState<AuctionListFilter>('Live')
  const { data, isLoading, error } = useAuctions(filter)
  useGroup('lobby')

  return (
    <>
      <section className="hero">
        <div>
          <h1>Used shipping containers, auctioned live</h1>
          <p className="muted">Prices update the moment anyone bids. A bid in the last 30 seconds adds time, so nobody wins by sniping.</p>
        </div>
      </section>

      <div className="tabs" role="tablist">
        {TABS.map(t => (
          <button key={t.filter} role="tab" aria-selected={filter === t.filter}
                  className={filter === t.filter ? 'active' : ''} onClick={() => setFilter(t.filter)}>
            {t.label}
          </button>
        ))}
      </div>

      {error && <div className="error">{error.message}</div>}
      {isLoading && <p className="muted">Loading auctions…</p>}

      <div className="grid-cards">
        {data?.map(a => <AuctionCard key={a.id} auction={a} />)}
      </div>
      {data?.length === 0 && <p className="empty">Nothing here right now. New auctions are scheduled every few minutes.</p>}
    </>
  )
}
