// Mirrors the API's DTOs (see /swagger/v1/swagger.json).

export type AuctionStatus = 'Scheduled' | 'Live' | 'Ending' | 'Closed'
export type AuctionOutcome = 'Sold' | 'ReserveNotMet' | 'NoBids'
export type AuctionListFilter = 'Live' | 'Scheduled' | 'Closed'
export type UserRole = 'Bidder' | 'Admin'
export type MyBidState = 'Leading' | 'Outbid' | 'Won' | 'Lost'

export interface User {
  id: number
  email: string
  displayName: string
  role: UserRole
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: User
}

export interface AuctionSummary {
  id: number
  title: string
  containerType: string
  location: string
  condition: string
  yearBuilt: number
  currency: string
  startingPrice: number
  currentPrice: number | null
  minimumNextBid: number
  bidCount: number
  startsAt: string
  endsAt: string
  status: AuctionStatus
  hasReserve: boolean
  reserveMet: boolean
  leadingBidderId: number | null
  outcome: AuctionOutcome | null
}

export interface BidView {
  id: number
  amount: number
  bidderId: number
  bidderName: string
  placedAt: string
}

export interface AuctionDetail {
  summary: AuctionSummary
  description: string
  minIncrement: number
  scheduledEndsAt: string
  closedAt: string | null
  winnerName: string | null
  recentBids: BidView[]
}

export interface BidResult {
  bidId: number
  auctionId: number
  amount: number
  placedAt: string
  minimumNextBid: number
  endsAt: string
  extended: boolean
  replayed: boolean
}

export interface MyBid {
  auction: AuctionSummary
  myHighestBid: number
  myBidCount: number
  state: MyBidState
}

export interface CreateAuctionRequest {
  title: string
  description: string
  containerType: string
  location: string
  condition: string
  yearBuilt: number
  startingPrice: number
  minIncrement: number
  reservePrice: number | null
  startsAt: string
  endsAt: string
}

// SignalR messages

export interface BidPlacedMessage {
  auctionId: number
  bidId: number
  amount: number
  bidderId: number
  bidderName: string
  placedAt: string
  bidCount: number
  minimumNextBid: number
  endsAt: string
  extended: boolean
  reserveMet: boolean
  serverTime: string
}

export interface OutbidMessage {
  auctionId: number
  title: string
  newPrice: number
  minimumNextBid: number
  serverTime: string
}

export interface AuctionClosedMessage {
  auctionId: number
  title: string
  outcome: AuctionOutcome
  finalPrice: number | null
  winnerId: number | null
  winnerName: string | null
  serverTime: string
}
