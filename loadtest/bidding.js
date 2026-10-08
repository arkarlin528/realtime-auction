// k6 load test: many bidders hammering the same auction at once.
//
//   k6 run -e BASE_URL=http://localhost:8081 -e AUCTION_ID=1 -e TOKENS=tokens.txt loadtest/bidding.js
//
// tokens.txt holds one JWT per line (one per virtual bidder); create them by registering test
// users against your own local or staging environment. Never point this at someone else's server.

import http from 'k6/http'
import { check } from 'k6'
import { SharedArray } from 'k6/data'
import { Counter } from 'k6/metrics'

const tokens = new SharedArray('tokens', () => open(__ENV.TOKENS).split('\n').filter(Boolean))
const accepted = new Counter('bids_accepted')
const rejected = new Counter('bids_rejected_rules')
const conflicts = new Counter('bids_conflict')

export const options = {
  scenarios: {
    bidding_war: { executor: 'constant-vus', vus: 200, duration: '60s' },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'], // 409/422/429 are expected outcomes, not failures
    'http_req_duration{expected:yes}': ['p(95)<300'],
  },
}

http.setResponseCallback(http.expectedStatuses(200, 409, 422, 429))

export default function () {
  const base = __ENV.BASE_URL
  const auctionId = __ENV.AUCTION_ID
  const token = tokens[(__VU - 1) % tokens.length]

  const auction = http.get(`${base}/api/auctions/${auctionId}`).json()
  const amount = auction.summary.minimumNextBid + auction.minIncrement * Math.floor(Math.random() * 3)

  const res = http.post(`${base}/api/auctions/${auctionId}/bids`, JSON.stringify({ amount }), {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      'Idempotency-Key': `${__VU}-${__ITER}`,
    },
    tags: { expected: 'yes' },
  })

  if (res.status === 200) accepted.add(1)
  else if (res.status === 422) rejected.add(1)
  else if (res.status === 409) conflicts.add(1)
  check(res, { 'no server errors': r => r.status < 500 })
}
