# deploy — Podman Desktop (local, no cloud)

Rules: [`CLAUDE.md`](CLAUDE.md) · design: [doc 17](../docs/design/17-deployment-and-operations.md) · secrets: [ADR 0003](../docs/adr/0003-local-stack-compose-and-secrets.md).

```bash
deploy/xd-up.sh            # secrets from Keychain → build → start → wait healthy → print URLs
deploy/xd-up.sh --no-build # restart without rebuilding images
deploy/xd-up.sh --owner-reset  # also replace the owner's password from the Keychain and sign out everywhere
deploy/xd-down.sh          # stop (data kept);  --purge deletes all data
```

| Service | Purpose | URL / port |
|---------|---------|-----------|
| `xd-proxy` | Caddy: React app + reverse proxy to the API | http://localhost:8080 |
| `xd-proxy` extras | Serves `docs/prototype` at `/prototype/` (delivery plan at `/prototype/delivery/`) and `docs/site` at `/docs/`, read-only | via proxy |
| `xd-api` | ASP.NET Core API (OpenAPI docs at `/api/docs`) | via proxy |
| `xd-worker` | Background services (heartbeat for now) | — |
| `xd-migrate` | One-shot: database migrations, then creates the owner account if none exists | — |
| `xd-owner-reset` | On-demand one-shot (profile `tools`): password reset from the secrets | — |
| `xd-db` | PostgreSQL 18 + TimescaleDB 2.30 | localhost:5433 (dev override) |
| `xd-seq` | Log viewer (profile `ops`) | http://localhost:5341 |

## Signing in

Everything except `/health/*`, the login page and its assets needs a session (cookie `xd.sid`). The proxy also guards `/prototype/` and
`/docs/` (`forward_auth` → `/api/auth/verify`). The **owner account** is fixed and comes from the Keychain
(`xdrishti/auth/owner-email`, `owner-name`, `owner-password`):

- First start: `deploy/xd-up.sh` asks for missing items at a terminal (hidden password prompt). Without a terminal it stops and points to
  `scripts/auth-secrets.sh`, which you run in your own terminal to store or change them.
- Change the password: `scripts/auth-secrets.sh`, then `deploy/xd-up.sh --owner-reset` (signs out every session).
- The three values are mounted as file secrets into `xd-migrate` and `xd-owner-reset` only, never into `xd-api`.

Files: `compose.yaml`, `compose.dev.yaml`, `caddy/Caddyfile`, `db/init/` (roles, runs on first start), `xd-up.sh`, `xd-down.sh`.
