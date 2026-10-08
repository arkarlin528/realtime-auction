# ADR-0001: Optimistic concurrency (`xmin`) with retry for bids

- **Status:** Accepted
- **Date:** 2026-10-01

## Context
The core risk in an auction is **two bids that both saw the same price**. Alice and Bob both see $1,000 and both submit $1,050. If both are saved, the history shows two equal "winning" bids and the price no longer means anything. This gets worse in the last seconds, when most bids arrive.

The rule check ("≥ current + increment, not already leading, auction live") and the write must behave as one atomic step.

## Options considered
1. **Pessimistic lock:** `SELECT … FOR UPDATE` on the auction row inside a transaction. It's correct, but every bid queues on the lock, and a slow request holds everyone up.
2. **`SERIALIZABLE` isolation:** correct, but it fails with serialization errors that need the same retry handling, and it's harder to reason about.
3. **Single-writer queue per auction** (e.g. a Redis list or a channel per auction): very fast, but much more machinery, and the HTTP request becomes asynchronous.
4. **Optimistic concurrency on the auction row, with a retry.** Read, validate in the domain, then write with `WHERE xmin = @seen`. If 0 rows change, someone else won: reload and re-validate.

## Decision
**Option 4.**
- `Auction.Version` is mapped to PostgreSQL's built-in **`xmin`** system column (a `uint` with `IsRowVersion()`). It needs no extra column or trigger, and it changes on every update.
- `BiddingService.PlaceBidAsync` makes up to **5 attempts**. On `DbUpdateConcurrencyException` it clears the change tracker, reloads the auction, and runs `Auction.PlaceBid` again against the *new* price. Then one of three things happens:
  - The bid is still valid (it was higher anyway), so it is saved.
  - It's now too low, so the bidder gets **422** "Bid at least $1,100", which is exactly what happened.
  - After 5 lost races it returns **409** "auction is very busy, try again". In practice this is rare.
- No locks are held, so a slow client can't block other bidders.

## Consequences
- ✅ **Proven by integration tests against real PostgreSQL:**
  - `Twenty_bidders_with_the_same_amount_at_the_same_moment_produce_one_winner`: 20 parallel identical bids → exactly 1 accepted.
  - `Racing_bids_of_different_amounts_leave_a_consistent_history`: 30 parallel bids → the history, in save order, is strictly increasing by at least the increment, and `BidCount` matches.
- ✅ The rules stay in the domain model; the database only arbitrates who was first.
- ⚠️ Under extreme contention on one auction (thousands of bids per second), retries waste work. Option 3 (a per-auction queue) would then be the next step. For a container marketplace, bid rates are far below that.
- ⚠️ `xmin` is PostgreSQL-specific. On SQL Server the same design uses `rowversion` (see the logistics platform project).
