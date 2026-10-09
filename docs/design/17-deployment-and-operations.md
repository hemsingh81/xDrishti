# 17 — Deployment & Operations (Podman, local-only)

Everything runs in Podman containers on your Mac. No cloud service is required; internet is used only for
broker/exchange APIs.

## 1. Podman machine

- Containers run in a Linux VM (Podman machine). Use the **libkrun** provider on Apple Silicon — it gives
  containers GPU acceleration (Vulkan), which `xd-llm` needs.
- Size: planned **10 CPUs, 32 GB RAM, 250 GB disk**; the running machine is **10 CPUs, 24 GB, 120 GB** (decision Q8,
  2026-10-09: keep it until the xd-llm benchmark in P0-15; grow it before the local LLM or tick data need more).
- Static IP (decision Q6): not needed until automated orders (P12) — see §6.
- The 30B-class local LLM needs ≈ 18–22 GB ([doc 14](14-ai-assistant.md) §2), which does not fit beside the stack in 24 GB:
  **P0-15 must settle the machine size** before P9-01.
- Source code: **local git only** (decision Q7) — no remote yet. Keep a copy outside the Mac's internal disk
  (Time Machine or the external SSD) until a remote is chosen; `xd-backup` protects data, not code.
- The Mac must be awake for nightly jobs (and during market hours for later live services): no sleep on
  power, wired network, small UPS recommended.

## 2. Containers

| Container | Image basis | Profile | Notes |
|-----------|-------------|---------|-------|
| `xd-proxy` | Caddy (+ React build) | core | Only published HTTP port (127.0.0.1:8080 until login/TLS, then LAN); routes `/api`, `/health` (later `/hubs`, `/hangfire`); serves the docs read-only at `/prototype/` (clickable prototype, delivery plan at `/prototype/delivery/`) and `/docs/` (design-docs site); compression, security headers |
| `xd-migrate` | API image, `migrate` | core | One-shot: applies migrations, then exits |
| `xd-api` | .NET 10 ASP.NET Core | core | REST, SignalR, auth, assistant endpoint |
| `xd-worker` | .NET 10 + Hangfire | core | CSV import (watches `data/inbox`), EOD fetch, aggregation, account & portfolio sync, nightly pipeline, backtests, Lab runs, ML.NET training; scale replicas |
| `xd-feed` | .NET 10 | core | Market hours: Dhan WebSocket for the active set, quote polling for holdings, live bars & alerts (later also depth recording) |
| `xd-sandbox` | .NET 10 worker | core | User C# plugins; **no network**, read-only data, CPU/memory/time limits |
| `xd-db` | PostgreSQL + TimescaleDB (official image) | core | Named volume; read-only role for tools |
| `xd-seq` | Seq | ops | Logs |
| `xd-backup` | restic + pg_dump | ops | Nightly encrypted backup to external SSD |
| `xd-llm` | RamaLama / llama.cpp server | ai | GPU via libkrun; models volume |
| `xd-mcp` | .NET 10 | ai | Read-only MCP tools |
| `xd-hermes` | Own image, pinned Hermes Agent | ai (optional) | Only after PoC; hardened (doc 14) |
| `xd-exec` | .NET 10 | live (later) | Orders; isolated network; only holder of order-capable tokens |

Defined in `deploy/compose.yaml` with profiles `core`, `ops`, `ai`, `live`
(`podman compose --profile core --profile ops up -d`). Podman Desktop shows them as one application.

## 3. Networks

| Network | Members |
|---------|---------|
| `front` | proxy, api |
| `back` (internal) | api, worker, sandbox*, db, llm, mcp, seq, feed |
| `exec` (internal) | exec, db |
| Egress allowed | worker, feed, exec (broker/exchange hosts only) |

*`xd-sandbox` has no egress and no access except its job inputs/outputs.

## 4. Secrets

1. Source of truth: **macOS Keychain** (`xdrishti/<area>/<name>`: broker API keys/secrets per account, DB passwords, backup key).
2. `deploy/xd-up.sh` reads the Keychain (creating strong random DB passwords on first run) → writes secret files to
   `~/Library/Application Support/xDrishti/secrets` (private folder) → Compose mounts them as file secrets
   ([ADR 0003](../adr/0003-local-stack-compose-and-secrets.md)).
3. Containers receive only what they need (mounted at `/run/secrets/<ConfigKey>`, read by .NET KeyPerFile provider).
4. Never in images, compose files, environment dumps, logs or LLM context.

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
