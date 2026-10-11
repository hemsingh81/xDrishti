# 17 — Deployment & Operations (Podman, local-only)

Everything runs in Podman containers on your Mac. No cloud service is required; internet is used only for
broker/exchange APIs.

## 1. Podman machine

- Containers run in a Linux VM (Podman machine). Use the **libkrun** provider on Apple Silicon — it gives
  containers GPU acceleration (Vulkan), which `xd-llm` needs.
- Size: **10 CPUs, 24 GB RAM, 120 GB disk** (decision Q8; confirmed by the xd-llm benchmark in P0-15, 2026-10-10: with the 30B model loaded and
  the stack running the VM peaked at 5.3 GB because model weights live in GPU memory). Revisit the disk before tick-data storage (P13).
- Static IP (decision Q6): not needed until automated orders (P12) — see §6.
- The host Mac (48 GB) is already swapping heavily (28–42 GB of macOS swap in use in the sampled P0-15 runs); a resident 30B model adds ≈ 20 GB of GPU memory and
  loaded in 22–105 s (cause not established). `xd-llm` is therefore started on demand ([ADR 0005](../adr/0005-local-llm-runtime-and-models.md)).
- Disk budget: VM disk 120 GB (originally planned 250 GB) holds the images, databases and, if used, the 18.6 GB model volume; revisit before tick data (P13).
- Source code: **local git only** (decision Q7) — no remote yet. Keep a copy outside the Mac's internal disk
  (Time Machine or the external SSD) until a remote is chosen; `xd-backup` protects data, not code.
- The Mac must be awake for nightly jobs (and during market hours for later live services): no sleep on
  power, wired network, small UPS recommended.

## 2. Containers

| Container | Image basis | Profile | Notes |
|-----------|-------------|---------|-------|
| `xd-proxy` | Caddy (+ React build) | core | Only published HTTP port (127.0.0.1:8080 until login/TLS, then LAN); routes `/api`, `/health` (later `/hubs`, `/hangfire`); serves the docs read-only at `/prototype/` (clickable prototype, delivery plan at `/prototype/delivery/`) and `/docs/` (design-docs site) — both only for a signed-in owner (`forward_auth` to `/api/auth/verify`, otherwise redirect to `/login`); compression, security headers |
| `xd-migrate` | API image, `migrate` | core | One-shot: applies migrations, provisions the owner account when none exists, then exits |
| `xd-owner-reset` | API image, `owner-reset` | tools | On-demand one-shot (`xd-up.sh --owner-reset`): replaces the owner's password from the secrets, revokes all sessions |
| `xd-api` | .NET 10 ASP.NET Core | core | REST, SignalR, auth, assistant endpoint; the only holder of the broker vault key (`BrokerVault__Key`, refuses to start without it); `Brokers__PublicBaseUrl` = `http://localhost:${XD_HTTP_PORT}` (broker redirect URLs); networks `back` + `egress` (broker sign-in) |
| `xd-worker` | .NET 10 + Hangfire | core | CSV import (watches `data/inbox`), EOD fetch, aggregation, account & portfolio sync, nightly pipeline, backtests, Lab runs, ML.NET training; scale replicas |
| `xd-feed` | .NET 10 | core | Market hours: Dhan WebSocket for the active set, quote polling for holdings, live bars & alerts (later also depth recording) |
| `xd-sandbox` | .NET 10 worker | core | User C# plugins; **no network**, read-only data, CPU/memory/time limits |
| `xd-db` | PostgreSQL + TimescaleDB (official image) | core | Named volume; read-only role for tools |
| `xd-seq` | Seq | ops | Logs |
| `xd-backup` | restic + pg_dump | ops | Nightly encrypted backup to external SSD |
| `xd-llm` | llama.cpp server (pinned RamaLama image) | ai | GPU via libkrun (`/dev/dri`); model from the host folder or the volume `xd-models` (decide in P9-01); no published port; on demand |
| `xd-mcp` | .NET 10 | ai | Read-only MCP tools |
| `xd-hermes` | Own image, pinned Hermes Agent | ai (optional) | Only after PoC; hardened (doc 14) |
| `xd-exec` | .NET 10 | live (later) | Orders; isolated network; only holder of order-capable tokens |

Defined in `deploy/compose.yaml` with profiles `core`, `ops`, `ai`, `live`
(`podman compose --profile core --profile ops up -d`). Podman Desktop shows them as one application.

## 3. Networks

| Network | Members |
|---------|---------|
| `front` | proxy (the only published port), seq (ops) |
| `back` (internal) | api, worker, sandbox*, db, llm, mcp, seq, feed, proxy (to reach the api) |
| `egress` (not internal, nothing published) | **api only** (P1-16c) |
| `exec` (internal) | exec, db (later) |
| Egress allowed later | worker, feed, exec (broker/exchange hosts only) |

*`xd-sandbox` has no egress and no access except its job inputs/outputs.
`xd-api` joins `egress` for Dhan's sign-in ([ADR 0008](../adr/0008-broker-credentials-vault-and-connection.md), [0009](../adr/0009-dhan-consent-flow-only.md)): outbound only, no
published ports, no other member. The network itself does not filter hosts; the API's broker HTTP client does — it reaches only the
`generate-consent` and `consumeApp-consent` endpoints of `auth.dhan.co` (allow-list, HTTPS, no redirects, 15-second timeout;
doc 05 §8.1). No order, position or funds endpoint is callable.

## 4. Secrets

1. Source of truth: **macOS Keychain** (`xdrishti/<area>/<name>`: DB passwords, owner sign-in, broker vault key, backup key). Broker API keys,
   secrets and tokens are the exception: they are entered in the app and stored encrypted in the database (item 4).
2. `deploy/xd-up.sh` reads the Keychain (creating strong random DB passwords on first run) → writes secret files to
   `~/Library/Application Support/xDrishti/secrets` (private folder) → Compose mounts them as file secrets
   ([ADR 0003](../adr/0003-local-stack-compose-and-secrets.md)).
3. Containers receive only what they need (mounted at `/run/secrets/<ConfigKey>`, read by .NET KeyPerFile provider).
   The owner's sign-in credentials (`xdrishti/auth/owner-email|owner-name|owner-password`) are never generated: `xd-up.sh` asks for
   them at a terminal (hidden password prompt) or fails with a pointer to `scripts/auth-secrets.sh`. They are mounted only into
   `xd-migrate` (creates the owner when none exists) and the on-demand `xd-owner-reset` (profile `tools`, `xd-up.sh --owner-reset`),
   never into the running `xd-api`.
4. Broker credentials ([ADR 0008](../adr/0008-broker-credentials-vault-and-connection.md), replacing the credential references of ADR 0007):
   the owner enters each account's API key and secret in the app (write-only, step-up); the API stores them in `accounts.broker_secrets`
   encrypted with AES-256-GCM (account id + secret name as associated data, key version per row). The key material is the Keychain item
   `xdrishti/broker/vault-key`: `xd-up.sh` creates it (32 random characters) only when no such item exists — an item that exists but cannot
   be read stops the start instead of being replaced, because another key makes every stored secret unreadable — and writes the secret file
   `broker_vault_key`, mounted **only into `xd-api`** as `/run/secrets/BrokerVault__Key` (the 256-bit key is derived from it with
   HKDF-SHA256). Back it up with the database. The former `scripts/broker-secrets.sh`, `secrets/brokers.index` and the read-only
   `secrets/brokers` mount are gone (`xd-up.sh` deletes leftover files).
5. Never in images, compose files, environment dumps, logs or LLM context.

## 5. Operations

| Topic | Approach |
|-------|----------|
| Start at boot | launchd agent → `podman machine start` → `xd-up.sh`; services run with **no user logged in to the app** |
| Missed schedules | On start, services catch up missed runs (e.g. EOD fetch while the Mac slept) and reconcile desired state |
| Schedules | Stored in the database (`ops.service_schedules`), edited on the Services screen with dependency validation; applied to Hangfire recurring jobs in `xd-worker` without restart |
| Database migrations | EF Core migrations applied by the one-shot `xd-migrate` service (API image, `migrate` command, as `xd_owner`) before `xd-api`/`xd-worker` start; apps connect as least-privilege `xd_app` |
| Start / stop | `deploy/xd-up.sh` (secrets → build → start → wait healthy → print URLs), `deploy/xd-down.sh` (`--purge` deletes data) |
| URLs (local) | App http://localhost:8080 · API docs `/api/docs` · Seq http://localhost:5341 · PostgreSQL `localhost:5433` (dev override) |
| Upgrades | Versioned image tags; roll back by switching tag |
| Backups | Nightly `pg_dump` + models + config → restic on external SSD; monthly restore test |
| Health | ASP.NET health checks per container; failures → in-app notification |
| Logs | Serilog → Seq; container log rotation |
| Disk | Timescale compression; tick data (later) ~50–100 GB/year → external SSD volume |
| CSV inbox | Host folder `data/inbox/` bind-mounted into `xd-worker`; processed files moved to `data/processed/<job-id>/` |
| Market hours | `xd-feed` starts 09:00 and stops 15:35 on trading days (scheduled); Mac must be awake |
| Remote access | Off by default; optional WireGuard on home router |
| Dev loop | `dotnet watch` and Vite dev server on the host against containerised db/llm/ml; same images for "prod" |

## 6. Costs

| Item | Cost |
|------|------|
| Podman Desktop, .NET, React, PostgreSQL/TimescaleDB, Caddy, Seq (single user), LLM runtime & models | ₹0 |
| Dhan Data API (one account) | ≈ ₹590/month incl. GST |
| NSE files (optional reconciliation) | ₹0 |
| External SSD + UPS (one-time, recommended) | ≈ ₹10–18k |
| ISP static IP | Only when automated orders are enabled (P12); decision Q6: later, revisit at the P11 exit |
| Cloud | ₹0 — none used |
