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
| `xd-api` | ASP.NET Core API (OpenAPI docs at `/api/docs`); the only service with the broker vault key | via proxy |
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

## Broker credentials (vault)

The Dhan API key and secret (and the daily access token the consent login returns) are entered **in the app** when an account is added or on its Connection tab
(write-only, step-up) and stored encrypted in `accounts.broker_secrets` with AES-256-GCM ([ADR 0008](../docs/adr/0008-broker-credentials-vault-and-connection.md)).
The encryption key comes from the Keychain item `xdrishti/broker/vault-key`: `deploy/xd-up.sh` creates it on the first run (32 random characters) and
writes it as the secret file `broker_vault_key`, mounted **into `xd-api` only** as `BrokerVault__Key`. The API refuses to start without it.

- **Never delete or replace the vault key**: the stored secrets can only be read with the key that encrypted them. If the Keychain item exists but
  cannot be read (locked Keychain, denied access), `xd-up.sh` stops instead of creating a new one. Back it up together with the database.
- The redirect URL registered with the Dhan app is `http://localhost:${XD_HTTP_PORT:-8080}/api/brokers/dhan/callback` (`Brokers__PublicBaseUrl`; ADR 0009). `xd-api` reaches only `auth.dhan.co` (egress network) and, for the instrument master (ADR 0010), the two public files `images.dhan.co/api-data/api-scrip-master-detailed.csv` and `nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv` (allow-listed in the API; no credentials). The daily refresh is `XD_INSTRUMENTS_AUTO_REFRESH` (default `true`) at `XD_INSTRUMENTS_REFRESH_TIME_IST` (default `08:45`, India time).
- The credential references of ADR 0007 (`scripts/broker-secrets.sh`, `secrets/brokers/`, `brokers.index`) are gone; `xd-up.sh` deletes leftover files.
  Old Keychain items `xdrishti/broker/<ref>/…` are no longer read and can be removed in Keychain Access.

Files: `compose.yaml`, `compose.dev.yaml`, `caddy/Caddyfile`, `db/init/` (roles, runs on first start), `xd-up.sh`, `xd-down.sh`.
