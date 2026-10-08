import { Link } from 'react-router'
import { useMyBids } from '../api/hooks'
import type { MyBidState } from '../api/types'
import { Countdown } from '../components/Countdown'
import { formatMoney } from '../lib/bidding'
import { useGroup } from '../realtime/RealtimeContext'

const STATE: Record<MyBidState, { label: string; tone: string }> = {
  Leading: { label: 'Leading', tone: 'pill-success' },
  Outbid: { label: 'Outbid', tone: 'pill-warning' },
  Won: { label: 'Won', tone: 'pill-success' },
  Lost: { label: 'Not won', tone: 'pill-muted' },
}

export function MyBidsPage() {
  const { data, isLoading } = useMyBids()
  useGroup('lobby') // live prices for the auctions listed here

  return (
    <>
      <div className="page-head"><h1>My bids</h1></div>
      {isLoading && <p className="muted">Loading…</p>}
      {data?.length === 0 && <p className="empty">You haven't bid yet. <Link to="/">Browse live auctions →</Link></p>}
      {!!data?.length && (
        <div className="card table-wrap">
          <table>
            <thead>
              <tr><th>Auction</th><th>Status</th><th className="num">My highest</th><th className="num">Current</th><th>Time</th></tr>
            </thead>
            <tbody>
              {data.map(b => (
                <tr key={b.auction.id}>
                  <td><Link to={`/auctions/${b.auction.id}`}>{b.auction.title}</Link></td>
                  <td><span className={`pill ${STATE[b.state].tone}`}>{STATE[b.state].label}</span></td>
                  <td className="num">{formatMoney(b.myHighestBid, b.auction.currency)}</td>
                  <td className="num">{formatMoney(b.auction.currentPrice, b.auction.currency)}</td>
                  <td><Countdown startsAt={b.auction.startsAt} endsAt={b.auction.endsAt} closed={b.auction.status === 'Closed'} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}
