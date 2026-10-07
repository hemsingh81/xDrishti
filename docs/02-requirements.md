# 02 — Requirements

## 1. Functional requirements

### FR-1 Data
1. **Import 1-minute OHLCV from CSV files** (upload, watched folder, CLI) using saved column-mapping profiles;
   preview, validate, upsert with source precedence, report and undo per import job.
2. **Aggregate** 1-minute bars into 5m/10m/15m/30m/1h (09:15-aligned), 1D, 1W, 1M with identical rules for
   CSV and Dhan data; refresh after every import/fetch.
3. **End-of-day fetch** from the Dhan Data API for **all tracked instruments** (today's 1-minute + daily), gap
   backfill, instrument master refresh.
4. **Live data only for the active set** (open positions, armed tickets, context indices, optional pinned
   basket) via Dhan WebSocket; holdings via quote polling; live bars replaced by official bars at EOD.
5. Adjust for splits/bonus; keep raw prices for tradable levels.
6. Store context: indices, India VIX, breadth, sector indices, calendar, expiries, lot sizes, corporate actions.
7. Data-quality checks; fail closed (no plan) on critical failures.
8. **Data quality window**: per-instrument validation of completeness, bar sanity, spikes, stored-vs-recomputed
   aggregates and derived-vs-official daily, with inspect, repair and run history.

### FR-2 Instruments & baskets
1. Master list from Dhan's instrument master, searchable and filterable.
2. Tracked instruments: add/remove (single or bulk); activation triggers history backfill (CSV, then Dhan).
3. Baskets: manual, rule-based (dynamic) and system baskets; an instrument can be in many baskets;
   membership history kept.
4. Baskets define scopes for scans, Strategy Lab tests, reports, portfolio grouping and live watch.
5. Evaluation lenses: all trades, by basket, by instrument, by individual trade, by strategy, by account.

### FR-3 Brokers & accounts
1. Pluggable broker adapters behind common interfaces (Dhan in v1; Upstox and others later).
2. Any number of accounts, each with its own risk profile, styles, instruments and mode.
3. Each account has one or more **roles** — Data, Trading, Portfolio; exactly one primary Data account
   (optional standby with automatic failover); role changes audited and protected by password re-entry.
4. Read funds, positions, holdings, trade history and ledger per account (read-only in v1).
5. Auto-journal: match broker fills to recommendations.
6. Token lifecycle per account (Dhan 24 h tokens via API key/secret).

### FR-3b Access & operating model
1. Login required for every screen and API (username + password; no second factor — personal, LAN-only), idle timeout, lockout, password re-entry for sensitive actions.
2. All configuration and actions are persisted as desired state and executed by background services under a
   service identity — independent of any logged-in session or open browser.
3. Missed schedules (machine asleep) are caught up on start; jobs are idempotent and retried.
4. Services screen: status, schedule, last/next run, run now, pause/resume.

4. Service schedules editable in the UI (type, time, days, window, interval, run-after, retries, timeout,
   catch-up, parameters) with dependency & timing validation; automatic data failover to a standby account.

### FR-3c Human-in-the-loop decisions
1. Next-day tickets are **proposed**; you approve, modify (re-validated) or reject; unreviewed tickets lapse at
   the review cutoff; only approved tickets are armed.
2. AI/engine suggestions (track instrument, basket membership, promotions, config changes) go to an inbox for
   approval, with reasons and expiry.
3. Reports compare system proposals vs your decisions.
4. Pre-trade checklist per ticket (data quality, holdings conflict, sector exposure, event risk).
5. Optional auto-approval at the cutoff for tickets above a probability threshold with every check passing
   (autonomy level *supervised* or higher); auto-approved tickets are marked and audited.

### FR-4 Portfolio monitoring
1. Build transactions, FIFO lots and positions from broker trade history, CSV imports and manual entries.
2. Per position/trade: added date, days held, quantity, average cost, LTP, value, unrealised/realised P&L,
   return %, day change, **XIRR**, CAGR, weight.
3. Per group (bucket, basket, account, strategy, sector, total): invested, value, returns, **XIRR**, benchmark
   XIRR and alpha, drawdown.
4. Portfolio buckets (exclusive, additive) and basket lens (overlapping).
5. Live values during market hours; daily valuation snapshots; alerts.
6. Per-account tiles and an Action center of positions needing a decision (rules in doc 15); price alerts
   evaluated by services.

### FR-5 Strategies & signals
1. Strategies declare timeframes, sides, styles, setup, trigger, entry, initial stop, filters, invalidation.
2. Multi-timeframe filters (bias from higher timeframe).
3. Output = conditional trade plans (trigger + validity window + invalidation).

### FR-6 Exits & risk
1. Exit policy library: fixed R:R, partials, ATR/structure/EMA trailing, pyramiding, time stop, session exit,
   max hold (≤ 5 sessions), event exit.
2. Exit chosen per setup cell (walk-forward), fitted per trade (volatility, structure, obstacles).
3. Fixed-fractional sizing with lot rounding; portfolio and per-account limits.

### FR-7 Learning
1. Walk-forward backtest of every strategy × timeframe × side × exit policy.
2. Per-cell statistics (Bayesian, recency-weighted, regime-conditioned).
3. Calibrated meta-model P(success) per candidate; must beat baselines.
4. Daily grading of primary, reserve and rejected recommendations.
5. Drift detection, auto-pause, re-qualification; cell lifecycle.
6. Hypothesis loop: one-variable experiments, tested offline and in shadow before promotion.
7. Learning changelog explaining every automatic change.
8. Learn from **actual executions**: grade on fills, decompose plan-vs-actual gap, per-liquidity slippage model,
   capacity score (doc 09 §9).
9. **Lessons** for every detected problem (what went wrong → diagnosis → fix → result) with revert (doc 09 §10).
10. **Learning screen** showing progress against a frozen-model counterfactual, learning health, setups and
    lessons (doc 09 §11).
11. **Tweak & re-run**: change learning knobs and replay walk-forward with honest comparison rules; shadow →
    apply with password; automatic rollback (doc 09 §12).
12. **Autonomy levels** (advisory / supervised / autonomous within limits) to reduce manual work while risk
    increases, new strategies, live execution and account changes always need you (doc 09 §13).
13. **Smart rules** learned from history and applied nightly: market gate (no-trade / reduced days),
    equity-curve throttle, meta-labelling (take / size), learned entry windows, correlation-aware plan (doc 09 §14).
14. **Automatic loss tags** on every losing trade, linked to lessons; **weekly learning digest**.

### FR-8 Selection & plan
1. Eligibility filters, duplicate merge (confluence), ranking by conservative expected R.
2. Constraints: max new entries/day, max open positions, heat, sector, correlation, net exposure, loss limits.
3. Per-account allocation (replicate or distribute) and sizing.
4. Primary plan + reserve list; optional pre-open gap validation.

### FR-9 Strategy Lab
1. Submit strategies at runtime: rules file, form, plain English (LLM-drafted, user-confirmed), C# plugin;
   choose a basket (or instruments) as the test universe.
2. Validate (schema, look-ahead lint), quick test, full walk-forward test, robustness checks.
3. Verdict (PASS / MARGINAL / FAIL) with reasons, KPIs, charts, comparisons, trade replay.
4. Test ledger with multiple-testing-adjusted thresholds; promotion only through paper stage.

### FR-10 Reports & UI
1. Next-day plan per account, journal, portfolio, performance for any date range with lenses and filters.
2. Reports landing overview with auto-insights; multi-basket comparison; pivot builder.
3. Visual analytics: equity/drawdown, heatmaps, exit comparison, MAE/MFE, calibration, selector effectiveness,
   Monte Carlo, learning changelog, cell lifecycle, data quality, discipline.
4. Config editor with validation, diff and versioned activation.
5. Exports: PDF/HTML/CSV.
6. Responsive layout from 360 px phones to 4K / ultrawide monitors.

### FR-11 AI assistant
1. Daily briefing and weekly review written from structured data.
2. Natural-language questions over data via read-only tools; numbers only from tool results.
3. Strategy drafting for the Strategy Lab; hypothesis proposals.

## 2. Non-functional requirements

| ID | Requirement |
|----|-------------|
| NFR-1 | Fully local on macOS (Apple Silicon) in Podman containers; no cloud dependency |
| NFR-2 | EOD fetch + nightly cycle < 45 min for ~200 tracked instruments; full re-learn < 4 h; CSV import ≥ 1 M rows/min |
| NFR-2b | Live updates < 2 s from tick to screen for the active set; live feed limited to the active set |
| NFR-3 | Reproducible: any plan/test regenerable from data snapshot + config version + model version |
| NFR-4 | No look-ahead: enforced by engine design and automated tests |
| NFR-5 | Explainable: reasons and statistics attached to every recommendation |
| NFR-6 | Config validated on load (JSON Schema); invalid config never activates |
| NFR-7 | Fail closed: incomplete data or failed stage ⇒ no plan, clear error |
| NFR-8 | Secrets never in code, config, logs or LLM prompts |
| NFR-9 | Personal use: single owner; login (password); LAN-only access; audit of logins, changes and approvals |
| NFR-11 | Services keep running when no one is logged in; UI is only a control plane |
| NFR-12 | Responsive UI from 360 px phones to 3840 px monitors with no horizontal page scroll |
| NFR-10 | Running cost ≈ ₹590/month (Dhan Data API); all software open source / free |
