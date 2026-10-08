# ADR-0004: Server-authoritative time and anti-sniping

- **Status:** Accepted
- **Date:** 2026-10-01

## Context
Two time problems:
1. **Client clocks are wrong,** sometimes by minutes. A countdown based on the laptop clock could say "10 s left" when the server has already closed the auction, or the reverse.
2. **Sniping:** a bot bids at 0.5 s left, and nobody has time to respond. Many marketplaces consider this unfair.

## Decision
**The server is the only judge of time.**
- `Auction.StatusAt(now)` and `PlaceBid` use the server clock (`TimeProvider`). The client countdown is display-only.
- The client estimates its offset from the server once at startup, from `GET /api/time` using the round-trip midpoint. It refines the offset from the `serverTime` field on every SignalR message, and then applies the offset to all countdowns.
- Countdowns share a single 250 ms ticker (`useSyncExternalStore`) instead of one interval per card.

**Anti-sniping, eBay-style "soft close":**
- A bid in the **last 30 seconds** moves `EndsAt` to *now + 30 s*. `ScheduledEndsAt` keeps the original time, so the UI can show "extended +45 s by late bids".
- The background closer settles an auction only when the server clock passes the current `EndsAt`. If a last-moment bid lands first, the close simply re-checks on the next tick (see [ADR-0001](0001-optimistic-concurrency-for-bids.md)).

**Testing time:** the integration tests replace `TimeProvider` with a `FakeTimeProvider`. A test jumps to "10 seconds before the end", bids, and asserts the extension, with no sleeps and no flaky timing.

## Consequences
- ✅ Every client agrees on what the server will accept.
- ✅ Bidding wars end naturally once nobody bids within 30 s.
- ⚠️ In theory an auction could be extended forever. Real bidding wars stop, and the demo bots stop after 2 minutes of overtime.
- ⚠️ JWT issuing uses the real wall clock (not `TimeProvider`), because token validation always checks real time.
