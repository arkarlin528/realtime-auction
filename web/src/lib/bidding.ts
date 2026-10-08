/** Pure helpers for the bid form, kept separate so they're easy to unit test. */

export function formatMoney(amount: number | null | undefined, currency = 'USD'): string {
  if (amount == null) return '—'
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    maximumFractionDigits: amount % 1 === 0 ? 0 : 2,
  }).format(amount)
}

/** Three one-tap amounts: the minimum, and one and two increments above it. */
export function quickBids(minimumNextBid: number, increment: number): number[] {
  return [0, 1, 2].map(steps => roundMoney(minimumNextBid + steps * increment))
}

export function roundMoney(value: number): number {
  return Math.round(value * 100) / 100
}

/** Client-side check for instant feedback. The server re-checks everything, so this is only a convenience. */
export function validateBid(amount: number, minimumNextBid: number, currency = 'USD'): string | null {
  if (!Number.isFinite(amount) || amount <= 0) return 'Enter an amount.'
  if (roundMoney(amount) !== amount) return 'Use at most 2 decimal places.'
  if (amount < minimumNextBid) return `Bid at least ${formatMoney(minimumNextBid, currency)}.`
  if (amount > minimumNextBid * 20) return 'That is much higher than the current price. Check the amount.'
  return null
}

export function newIdempotencyKey(): string {
  return crypto.randomUUID()
}
