# 0003 — Local stack: Docker Compose on Podman, secrets as files from the Keychain

- **Status:** Accepted
- **Date:** 2026-10-08

## Context
Doc 17 planned "Keychain → Podman secrets". The stack is defined with the Compose specification and run through
`podman compose`, which delegates to the `docker-compose` binary (best maintained, integrated with Podman Desktop).
Outside Swarm mode, `docker-compose` cannot reference Podman's secret store (`external: true` is Swarm-only), but it
mounts **file-based secrets** at `/run/secrets/<name>`.

## Decision
- Compose provider: `docker-compose` (pinned v5.6.0, in `~/.local/bin`) via `podman compose`.
- Secrets: the **macOS Keychain stays the source of truth** (`xdrishti/<area>/<name>`). `deploy/xd-up.sh` reads them
  (generating strong random values on first run) and writes them to
  `~/Library/Application Support/xDrishti/secrets` (folder `700`), which Compose mounts as file secrets.
- Containers receive only the secrets they need, mounted with the configuration key as file name
  (`/run/secrets/Database__Password`), read by .NET's KeyPerFile provider — never via environment variables.
- Database roles: `xd_owner` (owner, migrations via the one-shot `xd-migrate` service) and `xd_app` (runtime, DML only).
- Published ports bind to `127.0.0.1` until login and TLS exist.

## Consequences
- Secret values exist on disk in a private folder while the stack is installed; deleting the folder is safe — the next
  `xd-up.sh` recreates it from the Keychain.
- Rotating a password = update the Keychain item, change it in PostgreSQL, rerun `xd-up.sh`.
- If Podman-native secrets are needed later (e.g. `podman kube play`), only `xd-up.sh` and the compose `secrets:` block change.
