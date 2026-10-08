# ADR-0003: Scale out with a Redis backplane, without sticky sessions

- **Status:** Accepted
- **Date:** 2026-10-01

## Context
With more than one API instance, a bid handled by instance A must reach watchers whose WebSocket is connected to instance B. SignalR groups exist only in memory on each instance.

Also, SignalR's default connection flow is negotiate (HTTP) followed by connect, and both requests must reach the same instance. Normally that means **sticky sessions** at the load balancer.

## Decision
1. **Redis backplane** (`AddStackExchangeRedis`), switched on by setting `Redis:ConnectionString`. Each instance publishes group messages to Redis, and every instance forwards them to its own connections.
2. **WebSockets only, with negotiation skipped** in the client (`skipNegotiation: true, transport: WebSockets`). The whole realtime session is one upgraded HTTP request to one instance, so **any instance can take it** and nginx can plain round-robin. Every supported browser has WebSockets.
3. **Watching is anonymous; bidding is REST with a JWT.** The private "you've been outbid" message uses `Clients.User(sub)`, which works across instances through the backplane.
4. **Background jobs are safe to run on every replica:**
   - The auction closer is idempotent: `Close()` is a no-op once closed, and the `xmin` check lets only one instance's close win.
   - The demo bots go through the same concurrency-safe `BiddingService`.
5. **Migrations run exactly once** in a one-off `migrate` container before the replicas start. EF Core 8 has no migration lock, so two replicas migrating at the same time would race.

`docker-compose.yml` runs **2 API replicas + Redis + nginx** to demonstrate all of this locally.

## Consequences
- ✅ Horizontal scaling needs no load-balancer configuration.
- ✅ A single instance (local dev, tests) runs without Redis.
- ⚠️ Redis is now a dependency for multi-instance setups. If it's down, cross-instance messages are lost, although REST bidding still works. A refetch on reconnect covers missed messages.
- ⚠️ No long-polling fallback for very old proxies. That's acceptable for this audience.
