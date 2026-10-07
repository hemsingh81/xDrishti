# 01 — Overview

## 1. What xDrishti is

> **Name:** *xDrishti* — from Sanskrit *drishti* (दृष्टि), "vision, insight": seeing where your edge is, and
> where it is not. Short forms: `xd` (CLI), `xd-*` (containers), `XDrishti.*` (.NET projects), `xdrishti` (database,
> Keychain paths).

A **personal, self-learning trade-planning and portfolio-monitoring system** for Indian markets (NSE), running
entirely on your Mac in Podman containers.

**Data:** your 1-minute CSV files bootstrap history; the **Dhan Data API** keeps every tracked instrument up to
date after market close and streams live prices **only for what you're actively trading or holding**. All
timeframes (5m … 1h, 1D, 1W, 1M) are aggregated from 1-minute bars.

**Every evening it:**
1. fetches the day's 1-minute data for all tracked instruments and aggregates all timeframes,
2. syncs your broker accounts and grades how previous recommendations played out,
3. updates what it has learned about every *setup cell* (strategy × timeframe × side × style × exit × regime),
4. scans your **baskets** (e.g. MyChoice, Best stocks) for tomorrow's candidate trades,
5. estimates each candidate's probability of success and expected reward (in R-multiples),
6. keeps only the best few that fit your risk limits (e.g. max 3 new entries), sized per broker account,
7. publishes a next-day **trade plan** with entry trigger, stop, exit method, size and reasons.

**At any time you can:**
- add your broker accounts and choose what each is used for (data, trading, portfolio),
- review and **approve** the proposed next-day trades and AI suggestions (nothing is armed without you),
- manage the instruments you work on and group them into **baskets**,
- monitor your **portfolio** across accounts — when each trade was added, return %, **XIRR**, total value —
  grouped by basket, account, strategy or sector,
- evaluate results **by basket, by instrument, by individual trade or across all trades**,
- submit your own strategies in the **Strategy Lab** and get an honest, tested verdict.

You execute (or skip) the trades. The system learns from every outcome.

## 2. Core principles

| # | Principle | Consequence |
|---|-----------|-------------|
| 1 | **Predict whether a setup works, not where price goes** | Every output is a conditional trade plan with a probability and expected value |
| 2 | **The setup cell is the unit of learning** | Exit methods (1:2, 1:3, trailing, pyramiding…) are learned per cell and fitted per trade |
| 3 | **Rules generate, ML filters, risk rules decide** | Transparent strategies → calibrated ML probability → deterministic portfolio selection |
| 4 | **LLM is an analyst, never the trader** | All numbers come from the deterministic engine; LLM explains, answers, drafts |
| 5 | **Every recommendation is graded** | Including rejected candidates — proves the selector adds value |
| 6 | **Configuration over code** | Instruments, baskets, timeframes, strategies, exits, limits, accounts — all configurable |
| 7 | **Honest evaluation** | Walk-forward out-of-sample only, costs always on, paper stage before live |
| 8 | **Single code path** | Backtest, Strategy Lab, nightly scan and grading run the same engine code |
| 9 | **One source of bars** | Everything aggregates from 1-minute data (CSV or Dhan) with the same rules |
| 10 | **Live only where it matters** | Real-time data only for the active set; everything else end-of-day |
| 11 | **Local and cheap** | No cloud dependency; ≈ ₹590/month running cost (Dhan Data API) |

## 3. Trading styles

| Style | Entry timeframes | Holding | Forced exit |
|-------|------------------|---------|-------------|
| Intraday | 5m, 10m, 15m, 30m, 1h | Same session | Square-off time (e.g. 15:15) |
| Swing | 1h, 1D | 1–5 sessions | End of session N (N ≤ 5) |
| Long-term holdings | — | Your choice | Not managed by system exits (monitored in the portfolio) |

Both BUY and SELL. Overnight shorts use futures (cash shorts can't be carried overnight in India).

## 4. Fixed decisions

| Topic | Decision |
|-------|----------|
| Use | **Personal only** — one user, only your own broker accounts; no recommendations to others |
| Market | NSE: cash stocks + index/stock futures; options in a later phase |
| Historical data | **Your 1-minute CSV files** + Dhan historical API for gaps |
| Market-data API | **Dhan Data API only** (EOD for tracked instruments, live for the active set) |
| Accounts | Multiple accounts (Dhan; Upstox later), each with **roles**: Data · Trading · Portfolio — one account can hold several; exactly one primary Data account |
| Access | **Login required** (username + password; password re-entry for sensitive actions); single owner; every screen and API protected |
| Operating model | **Configure once, services run it** — background services do the work whether or not you're logged in |
| Insights | Pre-trade checklist, portfolio **Action center** per account, price alerts, data quality gate, report auto-insights |
| Learning visibility & autonomy | **Learning screen**: progress vs a frozen-model counterfactual, lessons (problem → fix → result), plan-vs-actual execution gap, tweak & re-run experiments; **smart rules** (no-trade days, equity-curve throttle, meta-labelling, learned entry windows, correlation-aware plan); grading on real fills; automatic loss tags; weekly digest; **autonomy levels** with auto-approval so you only review what matters |
| Decisions | **Human in the loop** — system/AI propose next-day tickets and instrument/basket suggestions; you approve, modify or reject |
| Instruments | Master list → tracked instruments → **baskets** (manual, rule-based, system) |
| Portfolio | Holdings, positions & trade history from Dhan + CSV/manual; FIFO lots; **XIRR**; buckets |
| Execution | v1 manual (system auto-journals your fills); assisted → semi-auto → auto in a later phase |
| Frontend | **React** (TypeScript) |
| Backend | **.NET** (ASP.NET Core, modular monolith) |
| Database | **PostgreSQL** with TimescaleDB extension |
| ML | **ML.NET** inside the .NET solution — no Python |
| LLM | Local open model in a GPU-enabled Podman container; optional Hermes Agent front-end after PoC |
| Deployment | **Podman Desktop** on Apple M4 Pro / 48 GB; no cloud |

Full rationale: [03-target-architecture.md](03-target-architecture.md).

## 5. Open questions

1. **CSV format** — please share one sample file so the import profile can be set up (column names, timestamp
   format, whether timestamps mark the bar start or end, raw or adjusted prices).
2. Trading capital and risk per trade (default 0.5% per trade, 1.5% daily loss limit).
3. Initial baskets and their purpose (e.g. MyChoice = intraday, MyLongTerm = holdings only, Best stocks = swing).
4. Multiple accounts: **replicate** the same trades across accounts or **distribute** them? (default replicate)
5. How far back should portfolio history go (Dhan trade history vs. older CSV/contract-note import)?
6. Willing to get a **static IP** from your ISP? Required only for automated order placement (later phase).

## 6. Definition of success (v1)

- Your CSV history imported; all tracked instruments updated every evening; all timeframes consistent.
- Portfolio shows every holding/trade with added date, return %, XIRR and value, grouped by bucket/basket/account.
- Nightly run completes unattended in < 30 min and publishes ≤ `max_new_entries_per_day` tickets per account.
- Each ticket: symbol, side, style, timeframe, trigger & validity window, stop, exit policy, size, P(win),
  expected R, plain-English reason.
- Results viewable for any date range by all trades / basket / instrument / trade / strategy / account.
- Your own strategies tested via the Strategy Lab without code changes.
- After ≥ 12 weeks of paper trading: positive expectancy after costs, consistent with backtest expectations.

## 7. Document map

| Doc | Content |
|-----|---------|
| [02 Requirements](02-requirements.md) | Functional & non-functional requirements |
| [03 Target architecture](03-target-architecture.md) | Tech stack, components, solution structure, data model, APIs |
| [04 Accounts, access & operations](04-accounts-access-and-operations.md) | Account roles, login, service-level operation, human-in-the-loop approvals |
| [05 Data & brokers](05-data-and-brokers.md) | CSV import, aggregation, Dhan EOD & live (active set), storage, quality |
| [06 Instruments & baskets](06-instruments-and-baskets.md) | Master list, tracked instruments, baskets, evaluation lenses |
| [07 Strategies](07-strategies.md) | Strategy anatomy, conditional plans, library, features, regimes |
| [08 Exits & risk](08-exits-and-risk.md) | R-multiples, exit policies, pyramiding, sizing, limits, costs |
| [09 Learning engine](09-learning-engine.md) | Cell stats, meta-model, walk-forward, drift, hypothesis loop |
| [10 Trade selection](10-trade-selection.md) | From N candidates to top 3, per-account allocation |
| [11 Strategy Lab](11-strategy-lab.md) | Runtime strategy rules, testing pipeline, verdict reports |
| [12 Portfolio monitoring](12-portfolio-monitoring.md) | Holdings, trades, returns, XIRR, buckets, live values |
| [13 Reports & UI](13-reports-and-ui.md) | React screens and report catalogue |
| [14 AI assistant](14-ai-assistant.md) | Local LLM roles, tools, guardrails, Hermes Agent decision |
| [15 Smart insights & automation](15-smart-insights-and-automation.md) | Pre-trade checklist, portfolio Action center, alerts, data quality gate, auto-insights |
| [16 Configuration](16-configuration.md) | Full trading config reference |
| [17 Deployment & operations](17-deployment-and-operations.md) | Podman topology, secrets, backups, costs |
| [18 Roadmap](18-roadmap.md) | Phases and exit criteria |
| [19 Risks & compliance](19-risks-and-compliance.md) | Pitfalls, safeguards, SEBI notes |
| [20 Extended modules](20-extended-modules.md) | Auto-execution, options, tick/depth data, fundamentals |
| [21 Glossary](21-glossary.md) | Terms |
