# 0001 — Foundation: stack, modular monolith, local-only, monorepo layout

- **Status:** Accepted
- **Date:** 2026-10-08

## Context
xDrishti is a personal, single-owner trading and portfolio system for NSE that must run entirely on one Mac
(no cloud), keep working while the owner is signed out, and learn from its own results. The design phase
(docs/design 01–21 and the clickable prototype) is complete; implementation starts with Phase 0.

## Decision
- **Stack:** React 19 + TypeScript + Vite (MUI, Lightweight Charts, ECharts) · .NET 10 (ASP.NET Core, Hangfire,
  SignalR, EF Core) · PostgreSQL + TimescaleDB · ML.NET in-process (no Python service) · local LLM via
  llama.cpp/RamaLama (optional).
- **Shape:** a modular monolith — one .NET solution with several hosts (Api, Worker, Feed, Mcp, Cli); engine
  modules are pure and I/O-free.
- **Runtime:** Podman Desktop containers on the owner's Mac; secrets from the macOS Keychain; LAN-only access;
  password login (no second factor while LAN-only).
- **Repository:** one monorepo — `backend/`, `frontend/`, `data/` (git-ignored), `deploy/`, `scripts/`,
  `docs/` (design, adr, prototype, site), `tools/`. Configuration and strategies are managed in the app, not as
  repository folders ([ADR 0002](0002-config-and-strategies-in-app.md)).

## Consequences
- One place for code, config and design; design docs are updated together with code.
- Single language for backend and ML keeps one code path for backtest, learning and live scoring.
- Remote access, a second factor and multi-user support are out of scope until a new ADR changes this.
