import { describe, expect, it } from 'vitest'
import { formatRemaining } from './clock'
import { formatMoney, quickBids, validateBid } from './bidding'

describe('quickBids', () => {
  it('offers the minimum and two steps above it', () => {
    expect(quickBids(1050, 50)).toEqual([1050, 1100, 1150])
  })

  it('avoids floating point noise', () => {
    expect(quickBids(0.1, 0.2)).toEqual([0.1, 0.3, 0.5])
  })
})

describe('validateBid', () => {
  it('accepts the minimum', () => {
    expect(validateBid(1050, 1050)).toBeNull()
  })

  it('rejects below the minimum with the amount needed', () => {
    expect(validateBid(1000, 1050)).toBe('Bid at least $1,050.')
  })

  it('rejects empty, negative and over-precise amounts', () => {
    expect(validateBid(Number.NaN, 100)).toBe('Enter an amount.')
    expect(validateBid(-5, 100)).toBe('Enter an amount.')
    expect(validateBid(100.123, 100)).toBe('Use at most 2 decimal places.')
  })

  it('catches a fat-fingered extra zero', () => {
    expect(validateBid(105000, 1050)).toMatch(/much higher/)
  })
})

describe('formatting', () => {
  it('formats money without needless cents', () => {
    expect(formatMoney(1250)).toBe('$1,250')
    expect(formatMoney(1250.5)).toBe('$1,250.50')
    expect(formatMoney(null)).toBe('—')
  })

  it('formats countdowns at the right precision', () => {
    expect(formatRemaining(0)).toBe('0s')
    expect(formatRemaining(9_100)).toBe('10s')
    expect(formatRemaining(125_000)).toBe('2m 05s')
    expect(formatRemaining(2 * 3600_000 + 5 * 60_000)).toBe('2h 5m')
    expect(formatRemaining(3 * 86400_000)).toBe('3d 0h')
  })
})
