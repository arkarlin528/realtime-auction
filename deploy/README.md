# Deploying to AWS EC2

The same approach as the logistics platform: Docker Compose on one EC2 host, Caddy for automatic HTTPS, and GitHub Actions to build, push to GHCR and roll out over SSH.

```
Internet ──443──▶ Caddy ──▶ nginx (React) ──/api, /hubs──▶ api ×2 ──▶ PostgreSQL
                                                         ╰──▶ Redis (SignalR backplane)
```

## One-time setup
1. **Instance:** Ubuntu 24.04. A **t3.small** (2 GB) is enough on its own; PostgreSQL is much lighter than SQL Server. Install Docker with `curl -fsSL https://get.docker.com | sudo sh`.
2. **Network:** open ports 22 (your IP only), 80 and 443. Point a DNS record such as `auction.yourdomain.com` at the Elastic IP.
3. **Files:** copy `deploy/docker-compose.prod.yml` and `deploy/Caddyfile` to `~/bidding`, then add a `.env` with the keys from `.env.example`, plus:
   ```
   GHCR_OWNER=<github-username-lowercase>
   DOMAIN=auction.yourdomain.com
   ```
4. **GitHub:** in the repo, add the secrets `DEPLOY_HOST`, `DEPLOY_USER` and `DEPLOY_SSH_KEY` to a `production` environment. Then set the repository variable `DEPLOY_ENABLED=true`.

### Sharing one instance with the logistics platform
Each stack has its own Caddy, and only one Caddy can own ports 80 and 443. Either:
- run a single Caddy with two site blocks (`logistics.…` → that stack's web, `auction.…` → this stack's web) on a shared Docker network; or
- use `HTTP_PORT` and `HTTPS_PORT` in `.env` to move this stack's Caddy, and put a front proxy in front.

The first option is simpler. Combined, the two stacks fit on a t3.medium.

## Operations
- **Logs:** `docker compose -f docker-compose.prod.yml logs -f api`
- **Scale the API:** `docker compose -f docker-compose.prod.yml up -d --scale api=3`
- **Reset the demo:** `docker compose -f docker-compose.prod.yml down -v && docker compose -f docker-compose.prod.yml up -d`
