import { Link } from 'react-router'
import type { AuctionSummary } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { formatMoney } from '../lib/bidding'
import { useFlashOnChange } from '../lib/useFlashOnChange'
import { ContainerArt } from './ContainerArt'
import { Countdown } from './Countdown'

const OUTCOME_LABEL = { Sold: 'Sold', ReserveNotMet: 'Reserve not met', NoBids: 'No bids' } as const

export function AuctionCard({ auction }: { auction: AuctionSummary }) {
  const { user } = useAuth()
  const flash = useFlashOnChange(auction.currentPrice)
  const leading = user != null && auction.leadingBidderId === user.id && auction.status !== 'Closed'

  return (
    <Link to={`/auctions/${auction.id}`} className={`card auction-card ${flash ? 'flash' : ''}`}>
      <div className="art-wrap">
        <ContainerArt type={auction.containerType} />
        {leading && <span className="pill pill-success corner">You're leading</span>}
        {auction.outcome && <span className={`pill corner ${auction.outcome === 'Sold' ? 'pill-success' : 'pill-muted'}`}>{OUTCOME_LABEL[auction.outcome]}</span>}
      </div>
      <h3>{auction.title}</h3>
      <p className="muted small">{auction.yearBuilt} · {auction.location}</p>
      <div className="price-row">
        <div>
          <span className="label">{auction.currentPrice != null ? (auction.status === 'Closed' ? 'Final price' : 'Current bid') : 'Starting at'}</span>
          <strong className="price">{formatMoney(auction.currentPrice ?? auction.startingPrice, auction.currency)}</strong>
        </div>
        <div className="right">
          <span className="label">{auction.bidCount} bid{auction.bidCount === 1 ? '' : 's'}</span>
          <Countdown startsAt={auction.startsAt} endsAt={auction.endsAt} closed={auction.status === 'Closed'} />
        </div>
      </div>
    </Link>
  )
}
