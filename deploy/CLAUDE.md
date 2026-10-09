# Deployment rules (Podman Desktop, local only)

- `compose.yaml` is the single definition of the stack; `compose.dev.yaml` adds developer access (DB port).
  Start/stop only with `xd-up.sh` / `xd-down.sh` (they prepare secrets from the macOS Keychain).
- Container names `xd-<role>`; core services have no profile; optional ones use profiles `ops`, `ai`, `live`.
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
  SPA fallback. Add new backend paths (e.g. `/hubs`) in `caddy/Caddyfile`.
- Changing a port, image or service → update `docs/design/17-deployment-and-operations.md`.
