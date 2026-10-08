# ADR-0006: Demo bots bid through the real services

- **Status:** Accepted
- **Date:** 2026-10-01

## Context
A public demo auction with nobody bidding shows nothing. Reviewers visit for two minutes and need to see prices move, countdowns extend and auctions close.

## Decision
A `DemoActivity` background service (on in the demo, off in tests) does two things:
- **Bot bidding.** Eight simulated bidders (`users.is_bot = true`, invented names, unusable random passwords, and refused by login) bid through **`BiddingService`**, the same code path as a human's HTTP request. They prefer auctions that are about to end, which triggers anti-sniping, and they back off once prices get high or overtime passes 2 minutes.
- **Supply.** It schedules new auctions through `AuctionLifecycle.CreateAsync` whenever fewer than 10 are open.

The seeder builds history the same way, through `Auction.PlaceBid`.

## Consequences
- ✅ What reviewers see is real behaviour: validation, concurrency retries, SignalR fan-out, and closing.
- ✅ A human reviewer can join in and get outbid by a bot, and they receive the private "you've been outbid" toast.
- ⚠️ Bots make the demo database grow. The reset in the deploy notes drops and reseeds it.
