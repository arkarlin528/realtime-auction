# ADR-0005: PostgreSQL, and the conventions used with it

- **Status:** Accepted
- **Date:** 2026-10-01

## Context
The logistics platform in this portfolio uses SQL Server. This project uses **PostgreSQL** on purpose, to show both: many modern teams (and the Laravel world) run PostgreSQL, and the concurrency features differ.

## Decision
- **Npgsql EF Core provider** with **snake_case naming** (`EFCore.NamingConventions`). Tables and columns read naturally in `psql` (`bids.idempotency_key`, not `"Bids"."IdempotencyKey"`).
- **Money is `numeric(12,2)`**, never floating point. The API validates at most 2 decimals.
- **`xmin` as the concurrency token** ([ADR-0001](0001-optimistic-concurrency-for-bids.md)).
- **Partial unique index** for idempotency keys (`WHERE idempotency_key IS NOT NULL`). This is a PostgreSQL feature that keeps the index small and lets bids without a key coexist.
- **Indexes match the queries:**
  - `(closed_at, ends_at)` for "live" and "due to close";
  - `starts_at` for "upcoming";
  - `(auction_id, amount DESC)` for the bid history.
- **Status is computed, not stored.** Scheduled, Live and Ending come from `starts_at`, `ends_at` and `closed_at` against the server clock, so no job has to flip a status column at the right second. Only *Closed* is persisted, because closing settles the outcome.

## Consequences
- ✅ No status column can drift out of sync with time.
- ✅ The same Clean Architecture code runs on either database; only Infrastructure differs (`IsUniqueViolation` checks the PostgreSQL error code `23505`).
- ⚠️ "Live" filtering compares against `now` in each query, which can't use a materialised status. With the indexes above that's fine at this scale.
