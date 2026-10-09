# deploy — Podman Desktop (local, no cloud)

Rules: [`CLAUDE.md`](CLAUDE.md) · design: [doc 17](../docs/design/17-deployment-and-operations.md) · secrets: [ADR 0003](../docs/adr/0003-local-stack-compose-and-secrets.md).

```bash
deploy/xd-up.sh            # secrets from Keychain → build → start → wait healthy → print URLs
deploy/xd-up.sh --no-build # restart without rebuilding images
deploy/xd-down.sh          # stop (data kept);  --purge deletes all data
```

| Service | Purpose | URL / port |
|---------|---------|-----------|
| `xd-proxy` | Caddy: React app + reverse proxy to the API | http://localhost:8080 |
| `xd-proxy` extras | Serves `docs/prototype` at `/prototype/` (delivery plan at `/prototype/delivery/`) and `docs/site` at `/docs/`, read-only | via proxy |
| `xd-api` | ASP.NET Core API (OpenAPI docs at `/api/docs`) | via proxy |
| `xd-worker` | Background services (heartbeat for now) | — |
| `xd-migrate` | One-shot database migrations | — |
| `xd-db` | PostgreSQL 18 + TimescaleDB 2.30 | localhost:5433 (dev override) |
| `xd-seq` | Log viewer (profile `ops`) | http://localhost:5341 |

Files: `compose.yaml`, `compose.dev.yaml`, `caddy/Caddyfile`, `db/init/` (roles, runs on first start), `xd-up.sh`, `xd-down.sh`.
