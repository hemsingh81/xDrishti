# 03 — Target Architecture

## 1. Architecture at a glance

**Style:** a **.NET modular monolith** (one codebase, clear module boundaries) deployed as a few
containers, with a **React** single-page app, **PostgreSQL + TimescaleDB** as the only database, **ML.NET** for machine
learning (no Python), and a **local LLM** — all on Podman Desktop.

Why a modular monolith (not microservices): one developer, one machine, shared engine code across
backtest / Strategy Lab / nightly scan / grading. Modules can be split into services later if ever needed.

```mermaid
flowchart TB
    subgraph Browser["Browser — Mac or phone on home LAN"]
        WEB["React SPA<br/>TypeScript · Vite"]
    end

    subgraph Podman["Podman Desktop · libkrun machine"]
        PROXY["xd-proxy · Caddy<br/>serves SPA · TLS · reverse proxy"]
        API["xd-api · ASP.NET Core<br/>REST · SignalR · auth · OpenAPI"]
        WORKER["xd-worker · .NET + Hangfire<br/>CSV import · EOD fetch · aggregation · portfolio sync ·<br/>nightly pipeline · backtests · Strategy Lab"]
        SANDBOX["xd-sandbox · .NET worker<br/>user C# plugins · no network"]
        MCP["xd-mcp · .NET MCP server<br/>read-only tools for LLM"]
        LLM["xd-llm · llama.cpp/RamaLama<br/>OpenAI-compatible · GPU"]
        DB[("xd-db · PostgreSQL + TimescaleDB")]
        VOL[("models volume<br/>ML.NET models · LLM weights")]
        SEQ["xd-seq · Seq<br/>logs"]
        BAK["xd-backup · restic"]
        FEED["xd-feed · .NET<br/>live feed for active set<br/>(later: depth recorder)"]
        EXEC["xd-exec · .NET (later)<br/>orders · kill switch"]
    end

    BROKERS[("Dhan APIs<br/>(+ NSE files)")]
    INBOX[/"CSV inbox<br/>1-minute files"/]

    WEB --> PROXY --> API
    API <--> DB
    API -->|enqueue jobs| DB
    WORKER <--> DB
    WORKER --> SANDBOX
    WORKER -->|train & save models| VOL
    API -->|load models| VOL
    API -->|chat| LLM
    LLM -. tool calls via API .-> MCP
    MCP -->|read-only role| DB
    WORKER -->|REST · EOD & history| BROKERS
    INBOX --> WORKER
    FEED -->|WebSocket · active set| BROKERS
    FEED -->|live prices| API
    EXEC -->|orders · static IP| BROKERS
    FEED --> DB
    EXEC <--> DB
    API & WORKER --> SEQ
    BAK --> DB
```

## 2. Technology decisions

### 2.1 Frontend — React

| Concern | Choice | Reason |
|---------|--------|--------|
| Framework | **React 19 + TypeScript + Vite** | Your choice; fast dev build |
| UI kit | **MUI (Material UI)** + MUI X Data Grid (community) | Mature components, dense data tables |
| Price charts | **TradingView Lightweight Charts** (open source) | Candles with entry/stop/target/trail overlays, trade replay |
| Analytics charts | **Apache ECharts** (`echarts-for-react`) | Heatmaps, calendar, Monte Carlo fans, scatter, large data |
| Server state | **TanStack Query** | Caching, refetch, pagination |
| Routing | React Router | — |
| Forms | React Hook Form + Zod | Strategy form builder, config forms |
| Code editor | **Monaco** + `monaco-yaml` with JSON Schema | Strategy rules & config editing with live validation |
| Real-time | `@microsoft/signalr` | Live prices & P&L for the active set, job progress, notifications |
| File upload | `react-dropzone` + chunked upload | CSV imports with preview |
| API client | Generated from OpenAPI (`orval`) | Typed client, no hand-written DTOs |
| Tests | Vitest + Testing Library; Playwright for E2E | — |

### 2.2 Backend — .NET

| Concern | Choice | Reason |
|---------|--------|--------|
| Runtime | **.NET 10 (LTS)**, C# | Your choice; LTS support window |
| Web | **ASP.NET Core** (Minimal API endpoint groups), OpenAPI | Lightweight, typed |
| Real-time | **SignalR** | Progress & notifications to React |
| Data access | **EF Core + Npgsql** for domain tables; **Npgsql binary COPY / Dapper** for bulk bars | EF for productivity, raw speed for time series |
| Background jobs | **Hangfire + Hangfire.PostgreSql** | Cron schedules, retries, queues, built-in dashboard; no extra infra |
| Validation | FluentValidation; **JsonSchema.Net** for YAML config/strategy | One schema shared with the React editor |
| YAML | YamlDotNet | Trading config & strategy rules |
| Strategy DSL | Own parser (Superpower) → **compiled LINQ expression trees** | Safe (no eval), fast, look-ahead-checked |
| Indicators | Own streaming implementations (`XDrishti.Indicators`, built in P3-01, [ADR 0031](../adr/0031-streaming-indicators-and-the-feature-frame.md)); cross-checked in the tests against **Skender.Stock.Indicators 2.7.3** (MIT, test-only, exact pin) | Point-in-time control, speed |
| Numerics | MathNet.Numerics | Statistics, Monte Carlo, Bayesian updates; own XIRR solver (Newton–Raphson + bisection) |
| CSV parsing | **Sep** or CsvHelper (streaming) + Npgsql binary COPY | Fast bulk import of 1-minute files |
| Spreadsheet reading | **ExcelDataReader 3.7.0** (MIT), `Infrastructure/Portfolio/SpreadsheetReader` behind `ISpreadsheetReader` (P2-17, [ADR 0029](../adr/0029-dhan-report-imports-and-source-precedence.md)) | Dhan's downloaded reports come as legacy `.xls` (BIFF8) and `.xlsx`; read natively, no conversion step; the Application layer never references the package |
| Live feed | `System.Net.WebSockets` client + binary packet parser (Dhan little-endian format) | Active-set streaming in `xd-feed` |
| Machine learning | **ML.NET** (LightGBM / FastTree trainers, PAV calibration, feature contributions) | Train and score in-process; one language, one code path |
| LLM client | **Microsoft.Extensions.AI** (OpenAI-compatible) | Tool calling against the local model |
| MCP server | **ModelContextProtocol C# SDK** | Read-only tools for the assistant / Hermes |
| HTTP resilience | Microsoft.Extensions.Http.Resilience (Polly) | Broker API retries, rate limits |
| Auth | ASP.NET Core Identity (single user, password) | Personal use; cookie auth |
| CLI | System.CommandLine (`xd` tool) | Strategy Lab and ops from terminal |
| Logging/telemetry | Serilog → **Seq**; OpenTelemetry metrics/traces | Local, searchable logs |
| Tests | xUnit, **Testcontainers** (real Postgres/Timescale), Verify snapshots | Engine correctness & look-ahead tests |

### 2.3 Data — PostgreSQL

| Concern | Choice | Reason |
|---------|--------|--------|
| Database | **PostgreSQL 17+** | Your choice |
| Time series | **TimescaleDB** extension (official image) | Hypertables + native compression for 1-min bars/ticks; **continuous aggregates** build 5m/10m/15m/30m/1h bars from 1m with 09:15 alignment; refreshed after CSV imports and EOD fetch |
| Queue | Hangfire tables in Postgres; `LISTEN/NOTIFY` for events | No separate broker (Redis/RabbitMQ) needed |
| Analytics | Plain SQL + materialised views for report aggregates | Single database to back up and query |
| Backups | `pg_dump` nightly + restic to external SSD | Simple, verifiable |

Estimated size: 1-min bars for ~200 symbols × 8 years ≈ 150 M rows → ~5–10 GB with Timescale compression.

### 2.4 ML — ML.NET only (no Python)

Our ML need is modest and well-defined: a **binary classifier on tabular features** (~10⁵–10⁶ rows,
~50–150 features) answering "does this candidate hit target before stop?", plus calibration and
explanations. ML.NET covers all of it:

| Need | ML.NET capability |
|------|-------------------|
| Gradient-boosted trees | `LightGbm` trainer (Microsoft.ML.LightGbm); `FastTree` (ML.NET's own GBDT) as fallback |
| Baselines | `LbfgsLogisticRegression`, `SdcaLogisticRegression` |
| Probability calibration | Calibrators incl. **PAV (isotonic)** and Platt; plus own reliability-curve check |
| Per-prediction explanation | `CalculateFeatureContribution` (supported for LightGBM/FastTree/linear models) |
| Global importance | Permutation Feature Importance (PFI) |
| Metrics | AUC, log-loss, Brier score via evaluation APIs + own walk-forward metrics |
| Persistence | Model `.zip` in the models volume + `research.model_versions` row; champion/challenger = DB pointer |

Flow: features and triple-barrier labels are computed by the same .NET code used for scanning → a
weekly Hangfire job trains, calibrates and validates per walk-forward fold → the model is saved and
registered → `PredictionEngine`/batch transform scores candidates in-process during the nightly run.

Why not Python: no second language/runtime, no extra container, no model-export hop, identical feature code
for training and scoring. What we give up — exact SHAP values, Optuna-style tuning, notebook ecosystem — is
not needed: feature contributions + PFI explain decisions, and the design deliberately avoids heavy tuning
(few parameters, plateaus).

**Platform check (Phase 0 spike):** containers on Apple Silicon are `linux/arm64`. Verify the LightGBM native
library loads there (if not, build `lib_lightgbm.so` from source in the worker image); fall back to the
managed `FastTree` trainer otherwise. The model sits behind an `IProbabilityModel` interface, so the trainer
can change without touching Planning or Learning.

### 2.5 AI

| Concern | Choice |
|---------|--------|
| Model server | **llama.cpp server** (pinned RamaLama image) in a container with GPU (libkrun); OpenAI-compatible API; started on demand ([ADR 0005](../adr/0005-local-llm-runtime-and-models.md)) |
| Model | Qwen3-30B-A3B at 4-bit (alternate gpt-oss-20b), 16K default / 32K supported context; no separate small model |
| Integration | Assistant endpoint in `xd-api` (Microsoft.Extensions.AI) + `xd-mcp` read-only tool server |
| Optional | Hermes Agent as hardened front-end after PoC ([doc 14](14-ai-assistant.md)) |

### 2.6 Infrastructure

| Concern | Choice |
|---------|--------|
| Containers | **Podman Desktop**, libkrun machine (GPU for LLM), `compose.yaml` with profiles |
| Proxy | **Caddy** — serves the React build, local TLS, routes `/api`, `/hubs`, `/hangfire` |
| Secrets | macOS Keychain → Podman secrets → .NET `KeyPerFile` configuration provider |
| Logs | Seq (free single-user) |
| Backups | restic → external SSD |

## 3. Modules (inside the .NET solution)

| Module | Responsibility |
|--------|----------------|
| **Market** | Bars, aggregation, context, calendar, corporate actions, data quality |
| **Data import** | CSV profiles, import jobs (preview, validate, upsert, undo), folder watcher |
| **Catalog** | Instrument master, tracked instruments, baskets (manual / rule / system), membership history |
| **Live** | Active-set computation, WebSocket subscriptions, live 1-minute bars, quote polling, live P&L & alerts |
| **Brokers** | Adapters (Dhan in v1; others later; NSE files) implementing `IMarketDataProvider`, `ILiveFeed`, `IAccountReader`, `ITokenProvider`, (later) `IOrderExecutor` |
| **Portfolio** | Transactions, FIFO lots, positions, buckets, valuations, returns, XIRR, benchmark, alerts |
| **Indicators & Features** | Streaming indicators, multi-timeframe features, regime labels |
| **Strategies** | Built-in strategies, DSL parser/compiler, plugin host |
| **Simulation** | Event-driven bar-by-bar engine: conditional entries, exits, pyramids, gaps, costs, slippage, MAE/MFE |
| **Learning** | Walk-forward runner, cell stats (Bayesian), exit selection, drift, lifecycle, hypothesis loop, ML.NET training & scoring, execution feedback, lessons, experiments (replay), autonomy rules |
| **Planning** | Scanner, scorer, selector, per-account allocator, pre-open check |
| **Trading** | Accounts, sync, journal, fill matching, outcomes grading, (later) execution |
| **Lab** | Strategy submissions, test runs, verdicts, test ledger |
| **Reporting** | Report queries with lenses (all / basket / instrument / trade / strategy / account), aggregates, exports |
| **Assistant** | LLM chat, tool routing, briefings |
| **Workflow** | Proposals, approvals (plan tickets, suggestions), review cutoff, suggestion inbox, pre-trade checklist |
| **Insights** | Portfolio action-center rules, price alerts, report auto-insights ([doc 15](15-smart-insights-and-automation.md)) |
| **Data quality** | Validation runs (completeness, sanity, spikes, stored vs recomputed aggregates, derived vs official daily), repairs, quality score |
| **Scheduling** | Service schedules (DB-stored), dependency & timing rules, run-after chains, catch-up, failover |
| **Platform** | Config versions, auth (Identity, password re-entry), audit, notifications, commands, service runs |

**Single code path:** Simulation + Strategies + Features are used identically by the Strategy Lab,
walk-forward learning, the nightly scanner and the outcome grader.

## 4. Repository & solution structure

One repository (monorepo) holds the code, configuration, deployment and design material.

```
xDrishti/
├── backend/                         # .NET 10 solution
│   ├── XDrishti.slnx                # solution (src/, tests/ folders)
│   ├── global.json                  # SDK pin + Microsoft Testing Platform
│   ├── Directory.Build.props        # shared build settings, analyzers, nullable, warnings-as-errors
│   ├── Directory.Packages.props     # central package versions
│   ├── Dockerfile                   # multi-stage: targets api, worker (chiseled, non-root)
│   ├── contracts/XDrishti.Api.json  # OpenAPI contract generated at build (source of frontend types)
│   ├── src/
│   │   ├── XDrishti.Domain/         # entities & value objects: Bar, Instrument, Signal, TradeTicket, RMultiple, Cell…
│   │   ├── XDrishti.Application/    # use cases, interfaces (ports), pipeline orchestration
│   │   ├── XDrishti.Infrastructure/ # EF Core, Npgsql COPY, Timescale, secrets, LLM client, model store, default seed
│   │   ├── XDrishti.Brokers.Dhan/   # REST + WebSocket feed + instrument master
│   │   ├── XDrishti.Brokers.Nse/    # optional reconciliation files
│   │   ├── XDrishti.DataImport/     # CSV profiles, parsers, import jobs
│   │   ├── XDrishti.Catalog/        # instruments, tracked list, baskets
│   │   ├── XDrishti.Portfolio/      # lots, positions, valuations, XIRR
│   │   ├── XDrishti.Indicators/     # streaming indicators, TimeframeBuilder, FeatureFrame (pure; references Domain only; P3-01)
│   │   ├── XDrishti.Strategies/     # built-ins + DSL (parser, compiler, look-ahead lint) + plugin host
│   │   ├── XDrishti.Simulation/
│   │   ├── XDrishti.Learning/       # cells, meta-model, smart rules, experiments, lessons
│   │   ├── XDrishti.Planning/
│   │   ├── XDrishti.Hosting/        # shared host defaults: KeyPerFile secrets, Serilog, app info
│   │   ├── XDrishti.Api/            # host: endpoints, SignalR hubs, auth, OpenAPI; `migrate` command
│   │   ├── XDrishti.Worker/         # host: Hangfire server, scheduled services
│   │   ├── XDrishti.Mcp/            # host: MCP read-only tool server
│   │   ├── XDrishti.Cli/            # `xd` command-line tool
│   │   ├── XDrishti.Feed/           # host: market-hours live feed for the active set (later: depth recorder)
│   │   └── XDrishti.Execution/      # host (later): order service
│   └── tests/                       # Domain/Application unit, Architecture (layer rules), Api.IntegrationTests (Testcontainers)
├── frontend/                        # React 19 + TypeScript + Vite + MUI; Dockerfile builds xd-proxy (Caddy + app)
│   └── src/{app, features/<name>, shared/{api,ui,lib}, theme, test}
├── data/                            # local runtime data, git-ignored: inbox/ (CSV drop, mounted into xd-worker), exports/, models/
├── deploy/                          # compose.yaml (+ compose.dev.yaml), caddy/Caddyfile, db/init, xd-up.sh / xd-down.sh
├── scripts/                         # check.sh (all gates), gen-api.sh, dev-api.sh, build-docs.sh, serve.sh
├── docs/
│   ├── design/                      # these design documents (Markdown source of truth)
│   ├── adr/                         # architecture decision records
│   ├── engineering/                 # engineering standards (SOLID, modularity, testing, performance, DoD)
│   ├── prototype/                   # clickable UX prototype (reference for the React app)
│   └── site/                        # generated HTML version of docs/design
└── tools/                           # docs-site builder, prototype vendoring
```

Trading configuration and strategies are **not repository folders**: they live in the database (versioned,
validated, audited) and are managed in the app; built-in defaults ship inside the backend and are seeded on first
start ([ADR 0002](../adr/0002-config-and-strategies-in-app.md)).

Dependency rule: `Domain` ← `Application` ← (`Infrastructure`, `Brokers.*`, engine modules) ← hosts.
Engine modules (Indicators, Strategies, Simulation, Learning, Planning) have **no I/O dependencies** —
they're pure and fully unit-testable.

### 4.1 `XDrishti.Indicators` as built (P3-01, [ADR 0031](../adr/0031-streaming-indicators-and-the-feature-frame.md))

The first engine module. A pure library that turns a stream of **closed 1-minute bars** into indicator values, closed higher-timeframe bars and session context; back-tests and live trading use the same code.

| Part | What it is |
|------|------------|
| `IIndicator` | `Update(in Bar)`, `Value`, `IsReady`, `WarmUp`, `Reset()`; constant (or amortised constant) work per bar, 0 bytes allocated in steady state. A value that is not ready is `NaN` with `IsReady = false` (the two always agree) |
| `IndicatorCatalog` | The 19 indicators with parameters (name, kind, min, max, default; at most 4), warm-up as a function of the spec (counted in bars of the spec's own timeframe), session behaviour (`Continuous` / `PerSession`), whether the source price is read, whether it is intraday-only, and a factory. The validator and lint of the DSL read it |
| `TimeframeBuilder` | Builds *closed* 5m, 10m, 15m, 30m, 1h and 1D bars with the rules of the stored aggregates ([doc 05 §4](05-data-and-brokers.md)); `Push` returns 0, 1 or 2 closed bars per minute; optional `TradingCalendar`; late bars are ignored and counted (`Ignored`) |
| `FeatureFrame` | Created once from a list of `FeatureSpec` (name, timeframe, price source, up to four parameters); validates them before any bar is fed, shares one instance between equal specs, builds only the needed builders, and exposes the value *as of the last closed bar of each timeframe* by a numeric handle. Counts the incomplete higher-timeframe bars it consumed (`IncompleteBarsConsumed`) |

**References and rules.** The project references `XDrishti.Domain` only (for `Bar`, `Timeframe`, `SessionClock`, `TradingCalendar`). Architecture tests keep it that way: no Application, Infrastructure, Hosting, EF Core, ASP.NET Core, Npgsql,
Serilog, `System.IO`, `System.Net`, `System.Data`, `System.Diagnostics`, `Environment`, `Random` or `TimeProvider`; no `DateTime.Now/UtcNow/Today` (scanned in the compiled code); no mutable static field; every public class
sealed or static. The only package outside the base class library that is involved is Skender.Stock.Indicators, and only in `XDrishti.Indicators.Tests`.

**Guarantees pinned by tests.** Each indicator equals an independent batch reference over seeded random series; `sma`, `ema`, `rsi` and `atr` also agree with Skender (differences: the ATR seed, and an RSI over a series that never
moves is 50 here and 100 in Skender); the *prefix property* (the output for the first *k* bars is the same when more bars are fed) over 220 seeds; the frame never shows the forming bar; two runs are bit-identical; `Reset()`
returns every indicator to its first state.

**Throughput.** The benchmarks are explicit tests, not part of `scripts/check.sh` (a solution run lists them as skipped). From `backend/`, in Release:

```
dotnet test --project tests/XDrishti.Indicators.Tests -c Release -- --explicit only --filter-class "*ThroughputBenchmarks*" --report-trx --results-directory <dir>
```

On an Apple-silicon Mac (best of 5 passes of 1,000,000 steps, through `IIndicator`): `sma(20)` 104 M steps/s, `ema(20)` 359, `rsi(14)` 109, `atr(14)` 209, `highest(20)` 64, `lowest(20)` 55, `vwap` 172, `rel_volume(20)` 199,
`or_high(15)` 208, `gap_pct` 182; `TimeframeBuilder` 109 M bars/s (5m) to 251 M (1D); a 12-feature frame 22 M steps/s, that is 0.045 µs per 1-minute step (targets: 10 M and 1 µs).

## 5. Data model (PostgreSQL schemas)

| Schema | Key tables |
|--------|-----------|
| `market` | `instruments`, `instrument_broker_ids`, `master_refreshes`, `bars_1m` (hypertable, source csv/dhan), `bars_1m_live`, `bars_1d`, continuous aggregates `bars_5m…bars_1h`, `context_daily`, `calendar`, `corporate_actions`, (later) `ticks`, `depth` |
| `catalog` | `tracked_instruments`, `baskets`, `basket_members` (valid_from/to), `index_membership` |
| `data` | `import_profiles`, `import_jobs`, `import_issues` |
| `portfolio` | `transactions`, `lots`, `lot_closures`, `positions`, `buckets`, `cash_flows`, `valuations_daily`, `corporate_action_adjustments` |
| `research` | `sim_runs`, `sim_trades` (with MAE/MFE, R, exit reason, feature snapshot), `cell_stats`, `exit_choices`, `regimes`, `training_sets`, `model_versions`, `hypotheses`, `learning_changelog`, `lessons`, `experiments`, `execution_gap` (per trade: plan vs fill), `autonomy_settings`, `smart_rules`, `meta_models`, `entry_windows`, `loss_tags`, `digests` |
| `trading` | `accounts`, `account_roles`, `account_snapshots`, `plans`, `plan_items` (primary/reserve/rejected), `outcomes`, `journal_entries`, `fills`, (later) `orders`, `order_events` |
| `lab` | `strategies`, `strategy_versions`, `lab_runs`, `lab_results`, `test_ledger` |
| `config` | `config_versions` (YAML, hash, author, activated_at), `active_config` |
| `ops` | `commands`, `service_schedules`, `service_runs`, `validation_runs`, `validation_results`, `data_quality_issues`, `audit_log`, `notifications` |
| `insights` | `alerts`, `action_items` (status: open / snoozed / done), `action_thresholds` |
| `workflow` | `approvals`, `suggestions` |
| `auth` | ASP.NET Core Identity tables (owner user, password hash, lockout) |
| `ai` | `conversations`, `tool_calls` (audit) |
| `hangfire` | job storage |

Every decision-relevant row carries `as_of` timestamps and the `config_version` / `model_version` used.

## 6. API surface (REST + SignalR)

| Area | Endpoints (examples) |
|------|----------------------|
| Instruments & baskets | `GET /api/instruments?q=&type=&sector=` · `POST /api/tracked` · `DELETE /api/tracked/{id}` · `GET/POST/PUT /api/baskets` · `POST /api/baskets/{id}/members` · `GET /api/baskets/{id}/preview` (rule) |
| Data import | `POST /api/imports` (upload) · `POST /api/imports/{id}/preview` · `POST /api/imports/{id}/commit` · `POST /api/imports/{id}/undo` · `GET/PUT /api/import-profiles` |
| Portfolio | `GET /api/portfolio?groupBy=&asOf=` (groupBy: bucket, basket, account, strategy, sector) · `GET /api/portfolio/positions/{id}` · `POST /api/portfolio/transactions` (manual/CSV) · `GET /api/portfolio/history?from=&to=` |
| Live | `GET /api/live/active-set` · `PUT /api/live/pins` |
| Plans | `GET /api/plans?date=&account=` · `GET /api/plans/{id}` · `POST /api/plans/{id}/preopen-check` |
| Journal | `GET /api/journal?from=&to=` · `PATCH /api/journal/{id}` (taken/skipped/notes) |
| Reports | `GET /api/reports/{type}?lens=&from=&to=&basket=&strategy=&tf=&side=&exit=&regime=&account=` · `GET /api/reports/{type}/export?format=` |
| Learning | `GET /api/cells` · `GET /api/cells/{key}` · `GET /api/changelog` · `POST /api/changelog/{id}/revert` · `GET/POST /api/hypotheses` · `GET /api/learning/journey` · `GET /api/lessons` · `GET /api/execution-gap` · `POST /api/experiments` (replay job, SignalR progress) · `POST /api/experiments/{id}/shadow` · `POST /api/experiments/{id}/apply` · `GET/PUT /api/autonomy` · `GET/PUT /api/smart-rules` · `GET /api/smart-rules/evidence` · `GET /api/digest/latest` · `GET /api/loss-tags` |
| Strategy Lab | `POST /api/lab/strategies` · `POST /api/lab/validate` · `POST /api/lab/runs` · `GET /api/lab/runs/{id}` · `POST /api/lab/runs/{id}/promote` |
| Config | `GET /api/config` · `POST /api/config/drafts` · `POST /api/config/drafts/{id}/validate` · `POST /api/config/drafts/{id}/activate` |
| Auth | `POST /api/auth/login` · `POST /api/auth/logout` · `POST /api/auth/reauth` |
| Accounts | `GET/POST/PUT /api/accounts` · `PUT /api/accounts/{id}/roles` (step-up) · `POST /api/accounts/{id}/test` · `POST /api/accounts/{id}/sync` |
| Approvals | `GET /api/plans/{date}/review` · `POST /api/plans/items/{id}/{approve, modify, reject}` · `POST /api/plans/{date}/approve-all` |
| Suggestions | `GET /api/suggestions?status=` · `POST /api/suggestions/{id}/accept` · `POST /api/suggestions/{id}/reject` |
| Services | `GET /api/services` · `PUT /api/services/{id}/schedule` (validated against dependency rules) · `POST /api/services/{id}/run` · `POST /api/services/{id}/pause` · `GET /api/services/{id}/runs` |
| Data quality | `GET /api/quality?status=` · `GET /api/quality/{symbol}` (checks, stored vs recomputed) · `POST /api/quality/run` · `POST /api/quality/{symbol}/repair` |
| Insights | `GET /api/portfolio/actions?account=&severity=` · `POST /api/portfolio/actions/{key}/{snooze, done}` · `GET/POST/DELETE /api/alerts` · `GET /api/reports/insights` |
| Risk profiles | `GET/POST/PUT/DELETE /api/risk-profiles` (step-up) |
| Data | `GET /api/data/quality` · `GET /api/data/coverage` |
| Assistant | `POST /api/assistant/chat` (streaming) |
| Real-time | SignalR `/hubs/events`: live prices & P&L (active set), alerts, job/import progress, nightly status. **Built in P2-10a ([ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md) §7, [spec](../specs/P2-10a-live-hub-and-live-portfolio-screen.md)):** the hub with server methods `Subscribe(scope)` / `Unsubscribe` and client events `pricesChanged` and `feedStatus`, plus `GET /api/live/status` (see below); alerts, job progress and nightly status use the same hub later |

## 7. Key flows

### Daily timeline (trading day)

| Time (IST) | What runs |
|------------|-----------|
| 09:00–15:35 | `xd-feed`: live data for the active set; quote polling for holdings; live P&L & alerts |
| 09:10 | Pre-open check arms approved tickets (after the 09:05 review cutoff) |
| ~15:50 | EOD fetch: today's 1-minute + daily for all tracked instruments, gap backfill, aggregates refresh |
| ~16:30 | Account sync: holdings, positions, trade history, ledger → portfolio & auto-journal; daily valuation |
| ~18:30 | Nightly pipeline (below) → next-day plan |

### Nightly pipeline (Hangfire recurring job, ~18:30 IST)
```mermaid
sequenceDiagram
    autonumber
    participant H as Hangfire
    participant M as Market/Brokers
    participant T as Trading
    participant L as Learning
    participant P as Planning
    participant U as API/SignalR → React
    H->>M: Verify EOD data complete · context · quality checks
    M-->>H: Fail closed on critical issues
    H->>T: Sync accounts (funds, positions, trades) · match fills
    H->>T: Grade primary / reserve / rejected items
    H->>L: Update cell stats · drift checks · changelog
    H->>P: Scan → score (ML.NET) → select → allocate per account
    P-->>U: Proposed plan + AI suggestions → review inbox · notification
    U->>P: You approve / modify / reject (before cutoff)
    Note over H,P: 09:10 pre-open check arms approved tickets → live active set
```
Weekly: retrain/recalibrate (ML.NET job), exit re-selection, hypothesis loop. Monthly: full walk-forward review.

### CSV import
Upload or drop files → profile detection & preview in React → commit → Hangfire job parses (streaming),
validates, maps symbols, bulk-COPYs into `bars_1m`, refreshes aggregates for the affected range → import report
via SignalR → instruments auto-tracked. Details: [05-data-and-brokers.md](05-data-and-brokers.md) §3.

### Live active set
Planning (armed tickets) + Portfolio (open positions, holdings) + context indices + pins → `xd-feed` diffs the set
and (un)subscribes on the Dhan WebSocket → ticks → live 1-minute bars + LTP → Portfolio/alerts → SignalR to React.

**Built in P2-10 ([ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md)):** `xd-feed` → ticks → last price per instrument (every 2 s) into the holdings, `market.live_quotes` and the price alerts → `NOTIFY live_prices` → (P2-10a) `xd-api` reads what changed and pushes it over SignalR. The feed and the API never call each other; the database is the only meeting point. Live 1-minute bars are not built yet.

**Built in P2-10a - the hub `/hubs/events` in `xd-api`.** Owner session cookie only (the same session middleware as the REST API, including the 30-minute idle timeout; a connection without one is refused with 401, and a connection is closed within 30 seconds after its session ends by logout, idle timeout or the 12-hour limit; an open connection does not keep a session alive, the screen's own requests do), same origin only (a request with another `Origin` is refused with 403; no CORS, no wildcard), WebSockets only, no anonymous method. A browser connects with `skipNegotiation` and the WebSockets transport (otherwise the `negotiate` POST needs the `X-CSRF-Token` header like every POST). **Server methods:** `Subscribe(scope)` where `scope = {accountId?, bucketId?, holdingIds?}` (the screen's scope with the meaning it has for `GET /api/holdings/summary`, plus the holdings shown; the server turns them into instruments, at most 1,000 instruments and 1,000 holding ids per connection) answers `{subscribed, truncated, failed}`; `Unsubscribe()`. A second `Subscribe` replaces the first. **Client events:** `pricesChanged {asOf, instruments:[{instrumentId, lastPrice, priceAt, source}]}` - first the current price of every followed instrument, then only changes; at most one burst a second per connection, at most 500 instruments and 32 KB per message (larger changes come as several messages); `source` is `live`, `quote` or `sync`. `feedStatus {state, subscribed, lastTickAt}` - on connect, on a change of `state` or `subscribed` (looked at every 5 seconds) and at least every 15 seconds, from the `xd-feed` heartbeat; `state` is the feed's own word or `not_running`. `GET /api/live/status` returns the same object. How it works: a hosted service in `xd-api` listens on `NOTIFY live_prices` (its own database connection, reopened by itself with a growing wait of 1 to 30 seconds after a database restart; a notification lost meanwhile is made up by one read when listening resumes), reads the current price of the instruments that connections follow (the holdings' price columns and `market.live_quotes`) and sends each connection what differs from what it was last sent; a read also happens every 30 seconds without a notification, because quote and sync prices are written without one. `xd-api` runs and stays healthy without the feed (the status then says `not_running`). The types of the hub are generated from the C# contract (`XDrishti.Application/Live/Push`) by `scripts/gen-api.sh` into `frontend/src/shared/realtime/hubContract.generated.ts`. Caddy proxies `/hubs/*` to `xd-api` (WebSocket upgrade is automatic in `reverse_proxy`).

### Strategy Lab run
React editor → `POST /api/lab/runs` → Hangfire job in `xd-worker` (plugins in `xd-sandbox`) → progress over
SignalR → results in `lab` schema → report page. Details: [11-strategy-lab.md](11-strategy-lab.md).

## 8. Security

- **Login required everywhere**: single owner, ASP.NET Core Identity (password), idle timeout, lockout, password re-entry for sensitive actions
  for sensitive actions; HTTPS via Caddy on LAN; optional WireGuard for remote access ([doc 04](04-accounts-access-and-operations.md) §2).
- **Service identity**: background services act on persisted desired state, not on user sessions; every action
  records the configuring actor for audit.
- Secrets per container via Podman secrets; only `xd-worker`/`xd-feed` (data/account tokens) and later
  `xd-exec` (order tokens) receive broker secrets.
- `xd-mcp` and the LLM use a **read-only database role**; no tool can change config, plans or orders.
- `xd-sandbox` has no network and a read-only data mount.
- Audit log for config activations, promotions, approvals and (later) orders.
