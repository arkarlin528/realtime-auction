import type {
  AuctionDetail, AuctionListFilter, AuctionSummary, BidResult, CreateAuctionRequest, LoginResponse, MyBid,
} from './types'

/** An RFC 7807 problem from the API, turned into one readable message. */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

let currentToken: string | null = null
let onUnauthorized: (() => void) | null = null

/** Wired up by the auth provider, so every request carries the token and a 401 signs the user out. */
export function configureAuth(token: string | null, unauthorized: () => void) {
  currentToken = token
  onUnauthorized = unauthorized
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  if (currentToken) headers.set('Authorization', `Bearer ${currentToken}`)

  let response: Response
  try {
    response = await fetch(path, { ...init, headers })
  } catch {
    throw new ApiError(0, 'Cannot reach the server.')
  }

  if (response.status === 401 && currentToken) onUnauthorized?.()
  if (!response.ok) throw new ApiError(response.status, await problemMessage(response))
  return (await response.json()) as T
}

async function problemMessage(response: Response): Promise<string> {
  if (response.status === 429) return 'Too many requests. Slow down a little.'
  try {
    const problem = (await response.json()) as { title?: string; errors?: Record<string, string[]> }
    if (problem.errors) return Object.values(problem.errors).flat().join(' ')
    if (problem.title) return problem.title
  } catch {
    // not JSON
  }
  return `Request failed (${response.status}).`
}

export const api = {
  time: () => request<{ now: string }>('/api/time'),

  login: (email: string, password: string) =>
    request<LoginResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  register: (email: string, displayName: string, password: string) =>
    request<LoginResponse>('/api/auth/register', { method: 'POST', body: JSON.stringify({ email, displayName, password }) }),

  auctions: (filter: AuctionListFilter) => request<AuctionSummary[]>(`/api/auctions?filter=${filter}`),
  auction: (id: number) => request<AuctionDetail>(`/api/auctions/${id}`),
  myBids: () => request<MyBid[]>('/api/me/bids'),
  createAuction: (body: CreateAuctionRequest) =>
    request<AuctionSummary>('/api/auctions', { method: 'POST', body: JSON.stringify(body) }),

  /**
   * The idempotency key is generated once per bid attempt by the caller and reused on retry,
   * so a request that timed out but actually succeeded can't place a second bid.
   */
  placeBid: (auctionId: number, amount: number, idempotencyKey: string) =>
    request<BidResult>(`/api/auctions/${auctionId}/bids`, {
      method: 'POST',
      body: JSON.stringify({ amount }),
      headers: { 'Idempotency-Key': idempotencyKey },
    }),
}
