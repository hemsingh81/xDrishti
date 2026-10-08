# xDrishti

*Drishti* (दृष्टि) is Sanskrit for **vision, insight** — seeing your trading edge clearly.

A **personal, self-learning trade-planning and portfolio-monitoring system** for NSE (India). It builds all
timeframes from 1-minute data (your CSV files + Dhan), learns which setups (strategy × timeframe × side × exit
method × market regime) are working, and publishes a short, risk-controlled **next-day trade plan** per broker
account. You organise instruments into **baskets**, monitor your **portfolio** (returns, XIRR, value by
basket/account), **approve** each day's proposed trades and test your own strategy rules at any time. Login-protected;
once configured, everything runs as background services.

**Target stack:** React (TypeScript) · .NET 10 (ASP.NET Core, Hangfire, SignalR) · PostgreSQL + TimescaleDB ·
ML.NET (training & scoring in-process) · local LLM (llama.cpp/RamaLama) · Podman Desktop — fully local, no cloud.
**Data & broker:** your 1-minute CSV files + Dhan Data API (EOD for tracked instruments, live only for active trades); multiple Dhan accounts.

> Status: design complete — implementation starts with Phase 0 ([roadmap](docs/design/18-roadmap.md)).

## Repository layout

```
xDrishti/
├── backend/      .NET 10 solution — src/XDrishti.* projects and tests/  (Phase 0 onwards)
├── frontend/     React 19 + TypeScript + Vite app
├── data/         local runtime data (CSV inbox, exports, models) — git-ignored
├── deploy/       Podman compose, Caddy, Keychain → secrets script, launchd
├── scripts/      developer scripts (build docs, serve prototype & docs)
├── docs/
│   ├── design/     design documents 01–21 (Markdown source of truth)
│   ├── adr/        architecture decision records
│   ├── prototype/  clickable UX prototype (offline, demo data)
│   └── site/       generated HTML version of the design docs
└── tools/        docs-site builder, prototype vendoring
```

Each folder has a short README describing what goes in it.

## Quick start

| To… | Do this |
|-----|---------|
| Try the planned UI | Open [`docs/prototype/index.html`](docs/prototype/index.html) — login is prefilled, press **Sign in**. Start with **Today**, **Plan review** and **Learning**; press **⌘K** to jump anywhere. |
| Read the design | Open [`docs/site/index.html`](docs/site/index.html) (search with `/` or `⌘K`, `[` / `]` between docs), or the Markdown in [`docs/design/`](docs/design/). |
| Serve both locally | `scripts/serve.sh` → prototype on http://localhost:8766, docs on http://localhost:8765 |
| Rebuild the HTML docs | `scripts/build-docs.sh` (after editing `docs/design/*.md`) |

## Design documents

| # | Document | Content |
|---|----------|---------|
| 01 | [Overview](docs/design/01-overview.md) | What it is, principles, fixed decisions, open questions |
| 02 | [Requirements](docs/design/02-requirements.md) | Functional & non-functional requirements |
| 03 | [**Target architecture**](docs/design/03-target-architecture.md) | Tech stack, components, solution structure, data model, APIs, flows |
| 04 | [Accounts, access & operations](docs/design/04-accounts-access-and-operations.md) | Account roles (data/trading/portfolio), login, services run it, human-in-the-loop approvals |
| 05 | [Data & brokers](docs/design/05-data-and-brokers.md) | CSV import, aggregation, Dhan EOD & live active set, storage, quality |
| 06 | [Instruments & baskets](docs/design/06-instruments-and-baskets.md) | Master list, tracked instruments, baskets, evaluation lenses |
| 07 | [Strategies](docs/design/07-strategies.md) | Conditional trade plans, strategy library, features, regimes |
| 08 | [Exits & risk](docs/design/08-exits-and-risk.md) | R-multiples, exit policies, pyramiding, sizing, limits, costs |
| 09 | [Learning engine](docs/design/09-learning-engine.md) | Cell stats, meta-model, walk-forward, drift, hypothesis loop |
| 10 | [Trade selection](docs/design/10-trade-selection.md) | Basket scan scope, top-N selection, per-account allocation |
| 11 | [Strategy Lab](docs/design/11-strategy-lab.md) | Test your own strategies at runtime |
| 12 | [Portfolio monitoring](docs/design/12-portfolio-monitoring.md) | Holdings & trades, returns, XIRR, buckets, live values |
| 13 | [Reports & UI](docs/design/13-reports-and-ui.md) | React screens, lenses and report catalogue |
| 14 | [AI assistant](docs/design/14-ai-assistant.md) | Local LLM roles, tools, guardrails, Hermes Agent decision |
| 15 | [Smart insights & automation](docs/design/15-smart-insights-and-automation.md) | Pre-trade checklist, Action center, alerts, data quality gate, auto-insights |
| 16 | [Configuration](docs/design/16-configuration.md) | Full trading config reference |
| 17 | [Deployment & operations](docs/design/17-deployment-and-operations.md) | Podman containers, secrets, backups, costs |
| 18 | [Roadmap](docs/design/18-roadmap.md) | Phases and exit criteria |
| 19 | [Risks & compliance](docs/design/19-risks-and-compliance.md) | Pitfalls, safeguards, SEBI notes |
| 20 | [Extended modules](docs/design/20-extended-modules.md) | Auto-execution, tick/depth data, options, fundamentals |
| 21 | [Glossary](docs/design/21-glossary.md) | Terms and further reading |

> **Personal use only.** Research and decision-support system, not investment advice. Validate with paper
> trading before using real capital.
