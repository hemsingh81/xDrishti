# Deployment rules (Podman Desktop, local only)

- `compose.yaml` is the single definition of the stack; `compose.dev.yaml` adds developer access (DB port).
  Start/stop only with `xd-up.sh` / `xd-down.sh` (they prepare secrets from the macOS Keychain).
- Container names `xd-<role>`; core services have no profile; optional ones use profiles `ops`, `ai`, `eval`, `live`.
- Networks: `front` (published), `back` (internal — database and workers live only here). Publish ports on
  `127.0.0.1` only, until login and TLS exist.
- Images: pin exact tags (never `latest`); build multi-stage; run as non-root; `read_only`, `cap_drop: [ALL]`,
  `no-new-privileges`, memory limits; every long-running service has a healthcheck; start order via
  `depends_on` conditions (db healthy → migrate completed → api healthy → proxy).
- Secrets: Keychain (`xdrishti/<area>/<name>`) → files in `~/Library/Application Support/xDrishti/secrets` →
  compose `secrets:` mounted at `/run/secrets/<ConfigKey>` (read by .NET KeyPerFile). Never in env vars, images, logs.
- Database: `xd_owner` owns schemas and runs migrations (`xd-migrate`); apps use `xd_app` (DML only).
  `db/init/*.sh` runs only when the volume is created.
- Proxy (Caddy): the only HTTP entry; compression, security headers, immutable caching for hashed assets,
  SPA fallback. `/hubs/*` (SignalR, WebSocket upgrade) and `/api`, `/health` go to `xd-api`; add new backend paths in `caddy/Caddyfile`
  (a test in `Api.IntegrationTests/Live/CaddyfileTests` and `scripts/check-proxy.sh` keep the hub route honest).
- Changing a port, image or service → update `docs/design/17-deployment-and-operations.md`.
- Execution service (P12-01b, ADR 0156): profile `live` = `xd-exec` only, never started by a plain `xd-up.sh`; only the owner's `xd-up.sh --exec` (which alone creates the Keychain item `xdrishti/db/exec`). Logs in as the database role `xd_exec` only; two secrets (`db_exec_password` as `Database__Password` and `exec_handover_key` as `ExecHandover__Key`, ADR 0171, held by `xd-api` and `xd-exec` only; the first also mounted into `xd-migrate` to set the role's login);
  network `exec` (internal) holds only `xd-exec` and `xd-db`; no published port, never on `egress`. Checked by `ExecComposeRulesTests` and `ExecutionBoundaryArchitectureTests`.
- Live readiness route (ADR 0172): `compose.exec-live.yaml` is read by `xd-up.sh` ONLY with `--exec` AND `XD_EXEC_READINESS_LIVE=true`. It adds `xd-exec-proxy` (profile `live`, tinyproxy built from `exec-proxy/` on Alpine pinned by tag and digest, the only service on both `exec` and `egress`; no secret, no database, no published port, no environment; read-only, `cap_drop ALL`, 32 MB)
  and gives `xd-exec` exactly `HTTPS_PROXY=http://xd-exec-proxy:8888`. The proxy allows `CONNECT` only to the lines of `exec-proxy/allowlist.txt` (today only `api.dhan.co:443`); a new line needs an accepted ADR and the same change in `ExecComposeRules.AcceptedHosts`. A deploy without the overlay removes the proxy container by its exact name (`podman rm -f xdrishti-xd-exec-proxy-1`; never `--remove-orphans`, which could touch the Hermes and eval containers); a running `xd-exec` keeps its proxy variable until the next `--exec` deploy recreates it, and fails closed meanwhile; `xd-down.sh` always includes the overlay. No other script may name the overlay.
- Eval (P9-09, ADR 0143): profile `eval` = four services (`xd-migrate-eval`, `xd-mcp-eval`, `xd-eval-seed`, `xd-eval`), never part of `xd-up.sh`, started with `scripts/xd-eval.sh`. None publishes a port; all sit on `back` only
  (no egress); `xd-mcp-eval` reads only the database `xdrishti_eval` as the two read-only roles with its own token (`eval_mcp_token`); only `xd-eval` holds that token, and `xd-api` holds no eval token and no path to
  `xd-mcp-eval`. Checked by `EvalComposeRulesTests`.
- Hermes PoC (P9-10, ADR 0144): profile `hermes` = `xd-hermes` only, never started by `xd-up.sh`, `xd-down.sh`, `xd-ai.sh` or `xd-eval.sh`; only the owner's `scripts/hermes-poc.sh up`. Network `ai-edge` is `internal` and holds only `xd-hermes`, `xd-llm`, `xd-mcp`, `xd-mcp-eval`
  (the last three also stay on `back`). Exactly two secrets (`llm_api_key`, `mcp_token_hermes`), no published port, no host mount, image `FROM` pinned by tag AND digest with no installer. Checked by `HermesComposeRulesTests`.
