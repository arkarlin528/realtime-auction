import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { useAuction, usePlaceBid } from '../api/hooks'
import type { AuctionDetail } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ContainerArt } from '../components/ContainerArt'
import { Countdown } from '../components/Countdown'
import { formatMoney, newIdempotencyKey, quickBids, validateBid } from '../lib/bidding'
import { useServerNow } from '../lib/clock'
import { useFlashOnChange } from '../lib/useFlashOnChange'
import { useGroup } from '../realtime/RealtimeContext'

export function AuctionRoomPage() {
  const id = Number(useParams().id)
  const { data, isLoading, error } = useAuction(id)
  useGroup(id)

  if (isLoading) return <p className="muted">Loading…</p>
  if (error || !data) return <div className="error">{error?.message ?? 'Auction not found.'}</div>
  return <Room detail={data} />
}

function Room({ detail }: { detail: AuctionDetail }) {
  const a = detail.summary
  const { user } = useAuth()
  const now = useServerNow()
  const flash = useFlashOnChange(a.currentPrice)
  const live = a.status !== 'Closed' && now >= new Date(a.startsAt).getTime() && now < new Date(a.endsAt).getTime()
  const leading = user != null && a.leadingBidderId === user.id
  const extendedBy = new Date(a.endsAt).getTime() - new Date(detail.scheduledEndsAt).getTime()

  return (
    <>
      <Link to="/" className="back">← All auctions</Link>
      <div className="room">
        <section className="card room-main">
          <ContainerArt type={a.containerType} size="lg" />
          <h1>{a.title}</h1>
          <dl className="facts">
            <div><dt>Type</dt><dd>{a.containerType}</dd></div>
            <div><dt>Condition</dt><dd>{a.condition}</dd></div>
            <div><dt>Built</dt><dd>{a.yearBuilt}</dd></div>
            <div><dt>Location</dt><dd>{a.location}</dd></div>
          </dl>
          <p className="muted">{detail.description}</p>
        </section>

        <aside className="room-side">
          <section className={`card bid-box ${flash ? 'flash' : ''}`}>
            <div className="bid-head">
              <span className="label">{a.currentPrice != null ? (a.status === 'Closed' ? 'Final price' : 'Current bid') : 'Starting price'}</span>
              <Countdown startsAt={a.startsAt} endsAt={a.endsAt} closed={a.status === 'Closed'} />
            </div>
            <strong className="price big">{formatMoney(a.currentPrice ?? a.startingPrice, a.currency)}</strong>
            <p className="muted small">
              {a.bidCount} bid{a.bidCount === 1 ? '' : 's'}
              {a.hasReserve && (a.reserveMet ? ' · reserve met ✓' : ' · reserve not yet met')}
              {extendedBy > 0 && a.status !== 'Closed' && ` · extended +${Math.round(extendedBy / 1000)}s by late bids`}
            </p>

            {a.status === 'Closed' ? (
              <ClosedBanner detail={detail} youWon={user != null && detail.summary.outcome === 'Sold' && a.leadingBidderId === user.id} />
            ) : !user ? (
              <Link className="btn wide" to="/login" state={{ from: `/auctions/${a.id}` }}>Sign in to bid</Link>
            ) : leading ? (
              <div className="notice success">You're the highest bidder.</div>
            ) : live ? (
              <BidForm auctionId={a.id} minimum={a.minimumNextBid} increment={detail.minIncrement} currency={a.currency} />
            ) : (
              <div className="notice">{now < new Date(a.startsAt).getTime() ? 'Bidding opens soon.' : 'Settling…'}</div>
            )}
          </section>

          <section className="card">
            <h2>Bid history</h2>
            <ol className="history">
              {detail.recentBids.map((b, i) => (
                <li key={b.id} className={i === 0 ? 'top' : ''}>
                  <span>{b.bidderId === user?.id ? 'You' : b.bidderName}</span>
                  <span className="muted small">{new Date(b.placedAt).toLocaleTimeString()}</span>
                  <strong>{formatMoney(b.amount, a.currency)}</strong>
                </li>
              ))}
              {detail.recentBids.length === 0 && <li className="muted">No bids yet. Be the first.</li>}
            </ol>
          </section>
        </aside>
      </div>
    </>
  )
}

function BidForm({ auctionId, minimum, increment, currency }: { auctionId: number; minimum: number; increment: number; currency: string }) {
  const placeBid = usePlaceBid(auctionId)
  const [amount, setAmount] = useState(String(minimum))
  const [error, setError] = useState<string | null>(null)
  const [extended, setExtended] = useState(false)
  const touched = useRef(false)

  // Follow the minimum as others bid, unless the user typed their own amount that is still high enough.
  useEffect(() => {
    setAmount(current => {
      if (touched.current && Number(current) >= minimum) return current
      touched.current = false
      return String(minimum)
    })
  }, [minimum])

  const submit = (value: number) => {
    const problem = validateBid(value, minimum, currency)
    if (problem) {
      setError(problem)
      return
    }
    setError(null)
    // One key per attempt; react-query's retry reuses it, so a lost response can't double-bid.
    placeBid.mutate({ amount: value, key: newIdempotencyKey() }, {
      onSuccess: result => {
        touched.current = false
        setExtended(result.extended)
      },
      onError: e => setError(e.message),
    })
  }

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    submit(Number(amount))
  }

  return (
    <form className="bid-form" onSubmit={onSubmit}>
      <div className="quick">
        {quickBids(minimum, increment).map(q => (
          <button key={q} type="button" className="btn btn-ghost" disabled={placeBid.isPending} onClick={() => submit(q)}>
            {formatMoney(q, currency)}
          </button>
        ))}
      </div>
      <div className="custom">
        <input type="number" inputMode="decimal" step={increment} min={minimum} value={amount}
               aria-label="Bid amount"
               onChange={e => { touched.current = true; setAmount(e.target.value) }} />
        <button className="btn" disabled={placeBid.isPending}>{placeBid.isPending ? 'Placing…' : 'Place bid'}</button>
      </div>
      {error && <div className="error">{error}</div>}
      {extended && !error && <div className="notice">Late bid: the auction was extended by 30 seconds.</div>}
    </form>
  )
}

function ClosedBanner({ detail, youWon }: { detail: AuctionDetail; youWon: boolean }) {
  const outcome = detail.summary.outcome
  if (youWon) return <div className="notice success">🎉 You won this container.</div>
  if (outcome === 'Sold') return <div className="notice">Sold to {detail.winnerName}.</div>
  if (outcome === 'ReserveNotMet') return <div className="notice">Closed. The reserve price wasn't met.</div>
  return <div className="notice">Closed with no bids.</div>
}
