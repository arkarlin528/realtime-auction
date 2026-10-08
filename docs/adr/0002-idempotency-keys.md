# ADR-0002: Idempotency keys on bids

- **Status:** Accepted
- **Date:** 2026-10-01

## Context
Mobile networks drop responses. If a bid request times out, the client can't know whether it was saved. Without protection, retrying it can create **a second bid that the user never intended**, at a higher price, possibly outbidding themselves.

## Decision
- The client sends an **`Idempotency-Key`** header: a UUID created once per bid attempt and reused on every retry of that attempt. React Query's retry reuses it automatically.
- The server stores the key on the bid. A **partial unique index** on `(bidder_id, idempotency_key) WHERE idempotency_key IS NOT NULL` makes the database the guarantee, not the application code.
- **Replay:** if the key was already used by this bidder, the original result is returned with `replayed: true` and nothing new is saved.
- **Race:** if two copies arrive at the same moment, one insert wins and the other hits the unique index (`23505`). It is turned into a replay of the winner.
- Keys are scoped per bidder, so one user's key can never collide with another's. They are at most 64 characters.

## Consequences
- ✅ Retries are always safe. This is covered by `Same_idempotency_key_never_bids_twice` and `Parallel_retries_with_one_key_still_make_one_bid`.
- ✅ This is the same pattern payment APIs use, so it's familiar to reviewers.
- ⚠️ Keys are kept forever with the bid. That's fine at this volume; a high-volume system would expire them after 24 h.
