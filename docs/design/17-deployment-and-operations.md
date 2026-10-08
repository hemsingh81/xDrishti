# 17 — Deployment & Operations (Podman, local-only)

Everything runs in Podman containers on your Mac. No cloud service is required; internet is used only for
broker/exchange APIs.

## 1. Podman machine

- Containers run in a Linux VM (Podman machine). Use the **libkrun** provider on Apple Silicon — it gives
  containers GPU acceleration (Vulkan), which `xd-llm` needs.
- Suggested size (Mac has 48 GB): **10 CPUs, 32 GB RAM, 250 GB disk**; tune in Phase 0.
- The Mac must be awake for nightly jobs (and during market hours for later live services): no sleep on
  power, wired network, small UPS recommended.

## 2. Containers

| Container | Image basis | Profile | Notes |
|-----------|-------------|---------|-------|
| `xd-proxy` | Caddy (+ React build) | core | Only published port (LAN); TLS; routes `/api`, `/hubs`, `/hangfire` |
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

1. Source of truth: **macOS Keychain** (broker API keys/secrets per account, DB passwords, backup key).
2. `deploy/xd-up.sh` reads Keychain → creates/updates **Podman secrets** → starts the stack.
3. Containers receive only what they need (mounted at `/run/secrets`, read by .NET KeyPerFile provider).
4. Never in images, compose files, environment dumps, logs or LLM context.

## 5. Operations

| Topic | Approach |
|-------|----------|
| Start at boot | launchd agent → `podman machine start` → `xd-up.sh`; services run with **no user logged in to the app** |
| Missed schedules | On start, services catch up missed runs (e.g. EOD fetch while the Mac slept) and reconcile desired state |
| Schedules | Stored in the database (`ops.service_schedules`), edited on the Services screen with dependency validation; applied to Hangfire recurring jobs in `xd-worker` without restart |
| Database migrations | EF Core migrations applied by a one-shot `xd-api migrate` step on deploy |
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
| ISP static IP | Only when automated orders are enabled (later) |
| Cloud | ₹0 — none used |
