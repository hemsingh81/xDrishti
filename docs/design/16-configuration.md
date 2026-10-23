# 16 — Configuration

## 1. Two kinds of configuration

| Kind | Where | Edited via | Examples |
|------|-------|-----------|----------|
| **Trading config** | Stored versioned in `config.config_versions` (seed: built-in defaults shipped with the backend) | React Config screen (Monaco + JSON Schema, diff, activate) | Scan scopes, data, strategies, exits, risk, accounts, learning, selection, portfolio |
| **Catalog data** | Database tables, edited in dedicated screens | Instruments, Baskets, Import screens | Tracked instruments, baskets & members, CSV import profiles (YAML seed optional) |
| **Infrastructure settings** | .NET `appsettings.json` + environment + Podman secrets | Files in `deploy/` | Connection strings, endpoints, ports, log levels |

Rules: JSON Schema validation before activation; every run records the active config hash; secrets are
referenced by name only (Keychain → Podman secrets).

## 2. Trading config (v1 defaults, illustrative)

```yaml
version: 1

market:
  exchange: NSE
  timezone: Asia/Kolkata
  session: { open: "09:15", close: "15:30" }

goals:
  target_expectancy_R: 0.20
  max_drawdown_pct: 8
  max_drawdown_action: halt_and_review

# Accounts and their roles are managed in the Accounts screen (stored in DB, password re-entry); shown here as seed.
# Dhan only (ADR 0008, 0009). API key and secret are entered in the app (write-only) and kept
# encrypted in accounts.broker_secrets — never in this file, the config versions or the repo.
accounts:
  - id: dhan_main
    broker: dhan                         # Dhan is the only broker
    client_id: "1000000001"              # required; shown masked
    api_key_issued_on: 2026-10-11        # keys are valid 12 months → warning 30 days before
    secrets: [api_key, api_secret]       # set / not set only; values live in the vault
    roles:
      data:      { enabled: true, primary: true }
      trading:   { enabled: true, mode: paper, profile: standard, styles: [intraday, swing],
                   instruments: { intraday: [EQ_MIS], swing_buy: [EQ_CNC], swing_sell: [FUT] },
                   capital: { source: broker_funds, cap: 1000000 } }
      portfolio: { enabled: true, include_in_totals: true }   # default bucket "Dhan · Main · Portfolio" is created automatically
  - id: dhan_longterm
    broker: dhan
    client_id: "1000000002"
    roles:
      portfolio: { enabled: true, default_bucket: Long term }    # optional: a custom bucket for imports; omitted = the account's own bucket
  - id: dhan_swing
    broker: dhan
    client_id: "1000000003"
    roles:
      data:      { enabled: true, standby: true }   # failover if primary token/subscription fails
      trading:   { enabled: true, mode: manual_live, profile: conservative, styles: [swing],
                   capital: { source: fixed, amount: 300000 } }
      portfolio: { enabled: true }

data_provider: { reconcile_with: nse_files }    # market data always from the primary Data account

access:
  idle_timeout_min: 30
  absolute_timeout_h: 12
  lockout: { attempts: 5, minutes: 15 }
  step_up_for: [account_roles, broker_secrets, config_activation, execution, data_deletion]

approvals:
  plan_review_cutoff: "09:05"
  unreviewed_policy: lapse                # lapse | arm_top_n
  allow_modifications: [reduce_size, tighten_stop, change_account, swap_with_reserve]
  suggestions: { expiry_days: 3, snooze_after_reject_days: 14 }

risk_profiles:                     # shared; edited on Settings → Risk profiles; accounts may override values
  standard:
    risk_per_trade_pct: 0.5
    max_capital_per_trade_pct: 25
    max_new_entries_per_day: 3
    max_open_positions: 5
    max_total_risk_pct: 3.0
    max_per_sector: 2
    max_pair_correlation: 0.70
    max_intraday: 2
    max_swing: 2
    daily_loss: { R: 2, pct: 1.5 }
    weekly_loss: { R: 5, action: halve_size }
  conservative:
    inherits: standard
    risk_per_trade_pct: 0.25
    max_new_entries_per_day: 2
    max_open_positions: 3
    daily_loss: { R: 2, pct: 1.0 }

allocation:
  mode: replicate                  # replicate | distribute
  max_accounts_per_symbol: 2
  max_total_risk_pct_all_accounts: 3.0

scan_scope:                        # which baskets the nightly plan scans (baskets are managed in the UI)
  - { basket: "Best stocks", styles: [intraday, swing] }
  - { basket: "MyChoice",    styles: [intraday] }
  - { basket: "MyLongTerm",  styles: [swing], sides: [BUY] }
  context_indices: [NIFTY 50, NIFTY BANK, INDIA VIX]
  filters: { min_price: 100, min_avg_turnover_cr: 50, exclude: { fno_ban: true, asm_gsm: true, results_within_days: 2 } }

catalog:
  auto_track_imported_instruments: true
  master_refresh: daily
  basket_membership_mode_default: current   # current | as_of (for reports)

data:
  base_resolution: 1m
  csv_import:
    inbox_path: data/inbox
    watch_inbox: true
    default_profile: my_1min_files
    precedence: [dhan_eod, csv, live]       # which source wins on overlap
    conflict_tolerance_pct: 0.1
  eod_fetch:
    time: "15:50"
    lookback_gap_days: 5
    backfill_years_on_track: 5               # Dhan intraday history limit ~5 years
    throttle_requests_per_sec: 5
  aggregation:
    timeframes: [5m, 10m, 15m, 30m, 1h, 1D, 1W, 1M]
    session_anchor: "09:15"
    daily_source: official                   # official | derived
    keep_partial_last_bar: true
    min_completeness_pct: 80
  adjust: { splits_bonus: true, dividends: false }   # bars stay raw; applied splits/bonus adjust on read (adjusted=true), dividends never (05 §4.2)
  quality: { max_missing_bar_pct: 2.0, spike_atr_mult: 8, stop_if_index_bad: true }

live:
  enabled: true
  session: { start: "09:00", end: "15:35" }
  active_set: { open_positions: true, armed_tickets: true, context_indices: true, pinned_baskets: [] }
  ws_mode: quote                             # ticker | quote | full
  max_ws_instruments: 200
  holdings_refresh_minutes: 5
  persist_live_bars: true

portfolio:
  sync: { time: "16:30" }                  # nightly sync of trades/ledger (later); the holdings refresh is set per account in the app (off / every N minutes / daily); acquisition dates are chosen at import (ADR 0012), doc 12 §3
  lot_method: fifo
  include_charges_in_cost: true
  default_bucket_for_plan_trades: account_default   # the Trading account's own "{name} · Portfolio" bucket, or a custom bucket name
  custom_buckets: []                                # no preset buckets; each Portfolio account has its own default bucket, custom ones are added in the app
  benchmark: NIFTY 50
  min_days_for_xirr: 30
  alerts: { max_position_weight_pct: 15, bucket_drawdown_pct: 10, results_within_days: 3, day_move_pct: 5 }
  action_center:                           # built in P2-09 (ADR 0022): stored in the database (platform.app_settings, key portfolio.action_thresholds), edited in Settings; ranges are fixed in code
    below_cost_pct: 10                     # 1-90; medium at or below -N %
    below_cost_high_pct: 20                # 2-95, above below_cost_pct; high at or below -N %
    gain_pct: 25                           # 1-1000
    overweight: { floor_pct: 15, multiple: 1.5, min_positions: 3 }   # floor 1-100, multiple 1-10, positions 2-50
    concentration_pct: 35                  # 5-100
    lagging: { pp: 10, days: 180 }         # points 1-100, days 30-3650
    long_term_window_days: 30              # 1-365
  price_alerts: { max_active: 200, checked: quotes_tick }   # one-shot; checked on the last price of every holdings-quotes tick (5 min in market hours)

timeframes:
  enabled: [5m, 10m, 15m, 30m, 1h, 1D]
  bias_map: { 5m: [15m, 1h], 10m: [30m, 1h], 15m: [1h, 1D], 30m: [1h, 1D], 1h: [1D], 1D: [1W] }

styles:                            # first_entry_time, last_entry_time, square_off_time and max_holding_sessions are the configuration spelling of the four time rules of a strategy (first_entry, last_entry, square_off, max_hold_sessions: doc 08 §5, doc 11 §2); a stored strategy carries its own, these numbers are not defaults for one that lacks a rule
  intraday: { enabled: true, first_entry_time: "09:30", last_entry_time: "14:30", square_off_time: "15:15" }
  swing:    { enabled: true, max_holding_sessions: 5, signal_valid_sessions: 1 }

strategies:                        # built-ins enabled by id; your own strategies live in the Strategy Lab library (database, `lab.strategy_versions`, P3-02d), each a YAML file of doc 11 §2 read by `XDrishti.Strategies.File` (P3-02c); the built-ins are seeded there from embedded text
  enabled: [ORB, VWAP_PULLBACK, EMA_PULLBACK, PDH_PDL_BREAK, BASE_BREAKOUT, TREND_PULLBACK, RSI2_REVERSION, NR7_ID_BO]

exits:                             # the strategy model (P3-02b, P3-02b2) checks every parameter below; the tables of FIXED_RR, PARTIAL_BE, PARTIAL_TRAIL, ATR_TRAIL, STRUCTURE_TRAIL, EMA_TRAIL, PYRAMID and time_stop are final (P4-02 ADR 0050, P4-03 ADR 0051, P4-04 ADR 0052, ADR 0036 Amendment 2) and run by the simulator, one candidate and one value in each list at a time (the two ladders of PYRAMID are not candidate lists); event_exit is ACCEPTED but PROVISIONAL until its slice (ADR 0036 Amendment 1) (PARTIAL_TRAIL is in doc 08 §2 only, not in this list); the verbs choose them with `--exit` and `--time-stop` and compare several candidates on the same entries (P4-05, [ADR 0053](../adr/0053-the-same-entries-comparison-and-the-mae-mfe-diagnostics.md))
  candidate_policies:
    - { id: FIXED_RR, rr: [1.5, 2.0, 2.5, 3.0] }
    - { id: PARTIAL_BE, book_pct: 50, t1_rr: 1.0, t2_rr: [2.0, 3.0] }              # every t2_rr above t1_rr
    - { id: ATR_TRAIL, atr_period: 14, mult: [2.0, 3.0], activate_at_rr: 1.0 }    # activate_at_rr is required (0: trailing from the entry); atr_period is the one default
    - { id: STRUCTURE_TRAIL, lookback_bars: 3 }
    - { id: EMA_TRAIL, length: 20 }                                                 # a scalar (doc 08 §2)
    - { id: PYRAMID, initial_pct: 50, add_at_rr: [1.0, 2.0], add_pct: [30, 20], max_adds: 2,
        trail_atr_mult: 2.0 }      # the cap of the total open risk at the initial 1R is always on and the trail is the ATR (atr_period optional, 14): cap_total_risk_to_initial and trail are not parameters
  overlays:                        # a listed overlay is on: `enabled` is not a parameter
    time_stop: { min_progress_rr: 0.5, intraday_bars: 6 }       # an intraday strategy gives intraday_bars (1 to the bars of a 375-minute session) ...
    # time_stop: { min_progress_rr: 0.5, swing_sessions: 2 }    # ... a swing strategy gives swing_sessions (1 to 5, below max_hold_sessions); never both
    event_exit: { days_before_results: 1 }                      # whole, 1 to 10
  selection_objective: { metric: mean_R_minus_downside, lambda: 0.5, prefer_plateau: true }
  per_trade_fit: { scale_by_atr: true, snap_to_structure: true, obstacle_check: true,
                   late_entry_target_reduction: { after: "13:30", factor: 0.7 } }

costs:                             # the broker's charge sheet: effective-dated schedules, every rate present (a zero is written 0, never null); built in P3-03c (ADR 0042); stored since P3-03f (ADR 0044) as one row of `platform.app_settings`, key `research.cost_sheet.<id>` (a new set of rates is a new id), loaded with `scripts/cost-sheet.sh load <file.json>` (`show`, `check` against your original report files; `--replace` overwrites an id), and chosen by every run: `--gross` or `--costs <id> [--capital] [--profile] [--max-cost-r]`
  id: "?"                          # lower case [a-z0-9][a-z0-9._-]{0,39}; an id starting "test-" is a synthetic sheet for tests and is never stored
  label: "?"
  source: "? where you read the figures, and when"
  checked: "?"                     # leave out until the sheet has been checked against your bills; a run says "not checked against bills" while it is absent
  schedules:                       # 1 to 12, strictly ascending by effective_from; the rates of a date are those of the last schedule on or before it
    - effective_from: "?"          # yyyy-MM-dd, 2000 to 2099
      gst_pct: 18                  # percent of brokerage + exchange + SEBI of the leg
      rounding: half_away_from_zero  # half_away_from_zero | half_even | down; each line is rounded to the paisa once
      intraday_equity:             # the same ten fields for delivery_equity; percents are percents (0.03 is 0.03 %)
        brokerage_pct: 0.03        # doc 16 value (intraday_pct); current? UNVERIFIED
        brokerage_cap: 20          # rupees per order (intraday_max); 0 means no brokerage and then the percent and the minimum must be 0
        brokerage_min: 0           # rupees per order; 0 means none
        stt_buy_pct: "?"           # was stt_ctt.eq_*: which side pays is the sheet's
        stt_sell_pct: "?"
        exchange_txn_pct: "?"
        sebi_per_crore: "?"        # rupees per 1,00,00,000 of turnover
        stamp_buy_pct: "?"
        other_pct: 0               # the "Oth. Charges" column of a bill
        dp_charge_per_sell: 0      # rupees per delivery sale (a flat figure; the depository's own lines vary)
      delivery_equity: { brokerage_pct: 0, brokerage_cap: 0, brokerage_min: 0, stt_buy_pct: "?", stt_sell_pct: "?", exchange_txn_pct: "?", sebi_per_crore: "?", stamp_buy_pct: "?", other_pct: 0, dp_charge_per_sell: "?" }   # delivery brokerage 0 is the doc 16 value
  slippage: { ticks: 1, bar_range_frac: 0.05, stop_exit_extra_ticks: 2, gap_fill: at_open }   # built in P3-03b (ADR 0038): the code defaults of SlippageConfig, stored with every run as canonical JSON; reading them from the configuration store is later

learning:
  walk_forward: { train_years: 3, test_months: 3, embargo_sessions: 5 }   # the later default for ~5 years of data (ISS-19)
  exit_walk_forward: { mode: expanding, min_train_months: 9, test_months: 3, embargo_sessions: 5, stable_folds_pct: 60 }   # the exit choice on ~2.1 years (P4-06, ADR 0054); overridden per run by --min-train-months, --test-months, --embargo-sessions, --lambda, --min-trades; stable_folds_pct is a new key: min_positive_folds_pct below keeps its meaning (qualification gates)
  min_trades: 30
  recency_half_life_days: 180
  bayes_prior: { type: beta, shrink_to_parent: true, strength: 20 }
  regime: { trend: { sma_days: 50, slope_days: 5, adx_period: 14, adx_min: 20 },       # built in P3-01c (ADR 0033): the numbers of ContextOptions
            volatility: { atr_period: 14, percentile_window: 252, low_below: 33.333333333333336, high_from: 66.66666666666667 } }  # exactly one third and two thirds (100/3, 200/3: the doubles of ContextOptions, never 33.33 / 66.67) of the mean of the VIX and index-ATR% percentiles; below low_below is LOW, from high_from is HIGH; pending owner confirmation, ADR 0033 Amendments 1, 2 and 13 (the owner's wording was 33.3 and 66.7)
  meta_model: { enabled: true, trainer: lightgbm, fallback_trainer: fasttree, split_by: [style, side],
                calibration: pav, must_beat_baseline: true }
  qualification_gates: { min_oos_trades: 30, min_expectancy_R: 0.15, min_profit_factor: 1.2,
                         min_positive_folds_pct: 60, max_drawdown_R: 10, max_calibration_error_pp: 5 }
  paper_days_before_active: 20
  drift: { window_trades: 20, below_band_checks: 3, action: pause }
  hypothesis_loop: { enabled: true, trigger: { schedule: weekly, min_new_trades_per_cell: 30, on_drift: true },
                     one_variable_only: true, max_open_hypotheses: 3, require_approval: per_autonomy }   # built in P6-10 (ADR 0076): one_variable_only and max_open_hypotheses are fixed in code (not read); require_approval = owner approves unless autonomy.level is autonomous (not stored yet); the weekly schedule waits for P7
  execution_feedback: { labels_from: fills, slippage_model: per_liquidity_bucket, reestimate: weekly,
                        min_turnover_cr: 50, capacity_score: true }                       # doc 09 §9
  lessons: { monitors: [drawdown_band, cusum_drift, calibration, execution_gap, decision_drag, tail_loss, data_quality] }
  experiments: { folds: 12, min_folds_better: 8, judge_after_live_gap: true, max_total_r_drop_pct: 10,
                 required_improvement_R: { base: 0.02, per_sqrt_trial: 0.006, window_days: 30 },
                 shadow_trades: 30, monitor_trades: 60, auto_rollback: true }             # doc 09 §12

smart_rules:                       # doc 09 §14 — edited on Learning → Smart rules; switching a rule off needs your password
  market_gate: { enabled: true, vix_reduce: 18, vix_no_trade: 22, breadth_min: 0.30, events: [rbi_policy, union_budget],
                 reduced: { max_items_factor: 0.5, risk_mult: 0.75 } }
  equity_throttle: { enabled: true, window_trades: 20, cut: 0.5 }
  meta_label: { enabled: true, trainer: lightgbm, min_take: 0.50, size_range: [0.5, 1.5] }
  entry_windows: { enabled: true, source: walk_forward, min_trades_per_hour: 80 }
  correlation: { enabled: true, max: 0.60, lookback_days: 60, include: [plan, open_trades, holdings] }
  grading: { basis: fills, keep_plan_r: true }
  loss_tags: { enabled: true, open_lesson_when: { tag_share_pct: 25, min_losses: 10 } }
  digest: { weekday: Sat, after: retrain, channels: [in_app] }

autonomy:                          # doc 09 §13 — edited on Learning → Autonomy; raising the level needs your password
  level: supervised                # advisory | supervised | autonomous
  auto_approve: { min_p_win: 0.58, all_checks_pass: true, at: review_cutoff }
  auto_apply_learning: true        # within guardrails: one variable, OOS proof, monitoring, auto-rollback
  max_auto_changes_per_week: 3
  always_ask: [risk_increase, new_strategy_live, enable_execution, account_changes]

selection:
  min_p_win: 0.45
  min_exp_R_lower: 0.10
  min_rr: 1.5
  max_cost_R: 0.10
  weights: { confluence: 0.05, regime_fit: 0.05, correlation: 0.10, event: 0.10 }
  conflict_policy: drop_if_close
  count_entries_on: trigger        # trigger | plan
  max_plan_items: 5
  reserve_size: 3

preopen_check: { enabled: true, gap_invalidate_atr: 0.5, promote_from_reserve: true }   # runs at 09:10 (services)

# Service schedules — stored in DB and edited on the Services screen; shown here as defaults
services:
  live-feed:        { schedule: { type: window,   start: "09:00", end: "15:35", days: trading }, params: { max_instruments: 200, mode: quote } }
  holdings-refresh: { schedule: { type: interval, every_min: 5, start: "09:15", end: "15:30" } }
  preopen:          { schedule: { type: daily, time: "09:10", days: trading }, params: { gap_invalidate_atr: 0.5 } }
  alerts:           { schedule: { type: interval, every_min: 1, start: "09:15", end: "15:30" } }
  eod-fetch:        { schedule: { type: daily, time: "15:50", days: trading }, params: { throttle_per_sec: 5, gap_lookback_days: 5 } }
  validation:       { schedule: { type: after, after: eod-fetch }, params: { days_to_check: 5, tolerance_pct: 0.05, spike_multiple: 8 } }
  portfolio-sync:   { schedule: { type: daily, time: "16:30", days: trading } }
  broker-history:   { schedule: { type: daily, time: "16:30", days: trading } }   # built in P2-13 (ADR 0025), service id portfolio-broker-history, "Portfolio trades & ledger": Dhan trades and ledger of the Portfolio accounts; first-start seed Portfolio:BrokerHistory:Enabled
  nightly:          { schedule: { type: daily, time: "18:30", days: trading } }
  suggestions:      { schedule: { type: after, after: nightly } }
  retrain:          { schedule: { type: weekly, weekday: Sat, time: "10:00" } }
  backup:           { schedule: { type: daily, time: "23:30", days: all }, params: { retention_daily: 30 } }
  defaults:         { retries: 2, timeout_min: 30, catch_up: true, notify_on_failure: true }

insights:                          # doc 15
  action_thresholds: { loss_pct: 10, gain_pct: 25, weight_pct: 15, sector_pct: 35, results_days: 7, lag_pp: 10 }
  pretrade_checks: { data_quality: block_with_confirm, holdings_conflict: warn, sector_limit: warn, results_within_days: 3 }
  alerts: { evaluate: services, channels: [in_app] }

strategy_lab:
  quick_test: { years: 1, symbols: 30 }
  default_test_method: walk_forward
  max_parallel_runs: 2
  # P5-02 reads these from configuration section StrategyLab (QuickYears, QuickSymbols, MaxParallelRuns) plus CostSheetId (the stored charge sheet a quick test is costed with, default `default`; gross when not stored), PollMilliseconds and RunWorker (ADR 0058)
  plugins: { enabled: true, sandbox: { cpu: 2, memory_gb: 4, timeout_min: 60 } }
  multiple_testing_adjustment: true
  # P5-03 reads StrategyLab:Gates (MinOosTrades, MinExpectancyR, MinProfitFactor, MinPositiveFoldsPct, MaxDrawdownR, MaxCalibrationErrorPp; the qualification_gates above are the defaults), StrategyLab:MinFoldsForPass (6; fewer walk-forward folds cap a verdict at MARGINAL) and StrategyLab:MultipleTesting (the switch above); the tightening is 1 + 0.25·log2(variants) (ADR 0059)
  # P5-04 reads StrategyLab:FullExits (array of --exit flags, default ["FIXED_RR:rr=2|1.5|2.5|3"]; must expand to 2 to 16 candidates, the first is the default exit) for the candidate exits of a full test.
  # P5-05 reads StrategyLab:Robustness (SweepParams 1 to 4, SweepSteps 1 to 4 positive factors, default [0.8, 0.9, 1.1, 1.2]; McDraws 100 to 20000, default 1000) for the robust method; the values are in the run key.
  assistant_can_run_tests: true
  assistant_can_promote: false

assistant:
  enabled: true
  model: "qwen3-30b-a3b"           # served by xd-llm
  context_tokens: 16384
  features: { daily_briefing: true, data_questions: true, strategy_drafting: true, hypotheses: true }
  hermes: { enabled: false }

schedule:
  nightly: "30 18 * * 1-5"         # IST
  weekly_retrain: "0 10 * * 6"
  monthly_review: "0 10 1 * *"

reports: { default_range_days: 90, weekly_pack: true }

# Later-phase modules (doc 20) — disabled until their phase
execution:     { enabled: false, broker_side_stop_required: true, max_orders_per_second: 2, price_band_pct: 2.0 }
feed_recorder: { enabled: false, depth_levels: 20, max_instruments: 50 }
options:       { enabled: false, underlyings: [NIFTY, BANKNIFTY], structures: [option_buy, debit_spread, credit_spread] }
fundamentals:  { enabled: false, quality_filter: { min_profit_quarters_of_last_4: 3, max_promoter_pledge_pct: 25 } }
```

## 3. Infrastructure settings (`appsettings.json` excerpt)

```json
{
  "ConnectionStrings": { "Db": "Host=xd-db;Database=xdrishti;Username=xd_app" },
  "Llm": { "Endpoint": "http://xd-llm:8080/v1" },
  "Models": { "Path": "/models" },        // ML.NET model files
  "Seq": { "ServerUrl": "http://xd-seq:5341" },
  "Secrets": { "Path": "/run/secrets" }
}
```
Passwords and the broker vault key (`BrokerVault__Key`, API only) are read from `/run/secrets/*` (Podman secrets) via the KeyPerFile provider.
Broker API keys, secrets and tokens are not configuration: they are entered in the app and stored encrypted in the database (ADR 0008).
`Brokers__PublicBaseUrl` (default `http://localhost:8080`, set from `XD_HTTP_PORT` in compose) is the base of the broker redirect URLs
`/api/brokers/<broker>/callback`.

### Instrument master (`Instruments:*`, ADR 0010, P1-03)

| Option (`appsettings` / environment) | Meaning | Default | Check |
|--------------------------------------|---------|---------|-------|
| `Instruments:AutoRefresh:Enabled` (`Instruments__AutoRefresh__Enabled`; compose: `XD_INSTRUMENTS_AUTO_REFRESH`) | **First-start setting** (ADR 0018): whether the master list is refreshed by itself once a day. Read once, when the service `instrument-master` has no stored row; after that the Services screen is the only place to change it | `true` | boolean |
| `Instruments:AutoRefresh:TimeIst` (`Instruments__AutoRefresh__TimeIst`; compose: `XD_INSTRUMENTS_REFRESH_TIME_IST`) | **First-start setting** (read once, like the switch above): time of the daily run as `HH:mm` (24-hour), **India time** (UTC+05:30, no daylight saving); Dhan publishes the file around 08:20 | `08:45` | required, validated at start (`ValidateOnStart`): a wrong value stops the API with a message |

The two source URLs are not configuration: they are fixed in the code and on the allow-list ([doc 05](05-data-and-brokers.md) §7.1, [doc 17](17-deployment-and-operations.md) §3). The manual "Refresh list" button works whether the daily run is on or off.

### End-of-day run (`Data:Eod:*`, ADR 0017 §2, ADR 0018)

| Option (`appsettings` / environment) | Meaning | Default | Check |
|--------------------------------------|---------|---------|-------|
| `Data:Eod:Enabled` (`Data__Eod__Enabled`) | **First-start setting**: whether the service `eod` is on. Read once, when it has no stored row; after that the Services screen is the only place to change it | `true` | boolean |
| `Data:Eod:TimeIst` (`Data__Eod__TimeIst`) | **First-start setting** (read once, like the switch above): time of the run as `HH:mm` (24-hour), **India time**; a run never starts before that day's session has ended | `15:50` | required, validated at start |

The other `Data:*` options (CSV folder and limits, Dhan fetch, gap look-back, data quality thresholds) are ordinary configuration and unchanged.

### Holdings refresh (`Portfolio:*`, ADR 0012, P2-01)

| Option (`appsettings` / environment) | Meaning | Default | Check |
|--------------------------------------|---------|---------|-------|
| `Portfolio:AutoRefresh:Enabled` (`Portfolio__AutoRefresh__Enabled`; compose: `XD_PORTFOLIO_AUTO_REFRESH`) | The API looks once a minute at the accounts whose own refresh setting is on and runs the due ones; `false` switches every schedule off ("Sync now" keeps working) | `true` | boolean |

Each account chooses its own schedule in the app (Accounts → Portfolio, and on the Portfolio screen): *Off* (default), *Every 5 / 15 / 30 / 60 minutes during market hours*
(Monday–Friday 09:15–15:30 India time) or *Daily at HH:MM IST*; it is stored in `accounts.portfolio_settings`, not in this file ([doc 04](04-accounts-access-and-operations.md) §1, [doc 12](12-portfolio-monitoring.md) §3).
The Dhan holdings endpoint (`GET https://api.dhan.co/v2/holdings`) is fixed in the code and on the allow-list ([doc 17](17-deployment-and-operations.md) §3).

### Trades and ledger from Dhan (`Portfolio:BrokerHistory:*`, [ADR 0025](../adr/0025-dhan-trade-history-and-ledger-sync.md), P2-13)

| Option (`appsettings` / environment) | Meaning | Default | Check |
|--------------------------------------|---------|---------|-------|
| `Portfolio:BrokerHistory:Enabled` (`Portfolio__BrokerHistory__Enabled`; compose: `XD_PORTFOLIO_BROKER_HISTORY`) | **First-start setting** (ADR 0018): whether the service `portfolio-broker-history` ("Portfolio trades & ledger", daily 16:30 IST on trading days) starts enabled; read once, when the service has no stored row, and ignored after it (then the Services screen decides). `false` starts it paused; "Sync trades & ledger" per account keeps working | `true` | boolean |
| `Portfolio:BrokerHistory:CallSpacingMs` (`Portfolio__BrokerHistory__CallSpacingMs`; no compose variable) | The least time between two calls to Dhan's trade history and ledger, kept by one throttle for the whole process; `0` switches the spacing off (tests) | `250` | integer 0 to 60,000 |

The window length (30 days), the page cap (200), the 30 s timeout and the 5 MB answer limit are fixed in the code, not configuration (tuned after the first real read). The two Dhan endpoints are on the allow-list ([doc 05](05-data-and-brokers.md) §8.3, [doc 17](17-deployment-and-operations.md) §3).

### Self-healing syncs (`Portfolio:Heal:*`, [ADR 0027](../adr/0027-self-healing-sync-and-actionable-notifications.md), P2-16)

| Option (`appsettings` / environment) | Meaning | Default | Check |
|--------------------------------------|---------|---------|-------|
| `Portfolio:Heal:RetryBackoffMinutes` (`Portfolio__Heal__RetryBackoffMinutes`; compose: `XD_PORTFOLIO_HEAL_BACKOFF_MINUTES`) | The waits before each quiet retry of a sync that failed for a reason that may pass (Dhan did not answer, its rate limit, an interrupted run), in minutes, comma-separated; their **number is the number of retries** of one failure, all inside the same India day. With the default the first retry is due 1 minute after the first failure, the second 5 minutes after the second, the third 15 minutes after the third. Empty switches the quiet retries off (the nightly and scheduled runs still happen) | `1,5,15` | up to ten waits of 1 to 999 minutes (`^\d{1,3}(,\d{1,3}){0,9}$`, blanks allowed) |
| `Portfolio:Heal:PersistDays` (`Portfolio__Heal__PersistDays`; compose: `XD_PORTFOLIO_HEAL_PERSIST_DAYS`) | The consecutive India days whose last attempt failed after which a failing sync (holdings, trades, ledger or the price poll) becomes an item in the bell; a reason only the owner can fix (the Dhan login) is told at once. The same number makes a failing price poll a failed run of the service | `2` | integer 1 to 30 |
| `Portfolio:Heal:DeepenMaxYears` (`Portfolio__Heal__DeepenMaxYears`; compose: `XD_PORTFOLIO_HEAL_DEEPEN_YEARS`) | How many years back from today the nightly deepening of the trade history reads at most, **one year per day**, while the trades do not explain a holding; `0` switches it off | `8` | integer 0 to 30 |
| `Portfolio:Heal:AttentionRetentionDays` (`Portfolio__Heal__AttentionRetentionDays`; compose: `XD_PORTFOLIO_HEAL_ATTENTION_DAYS`) | How many days a **resolved** item of the bell (`portfolio.attention_items`) is kept before the healer tick deletes it ([ADR 0030](../adr/0030-read-only-notices-retention-and-structured-failures.md), P2-18). Unresolved items are never deleted; a resolved item is not shown after 24 hours anyway (that is fixed in the code). The runtime role has DELETE on that one table only (migration `AttentionRetentionGrant`) | `30` | integer 1 to 3650 |

These are ordinary deployment settings, read at start (not first-start settings): change them in the environment and restart `xd-api`. The quiet retries and the catch-up after a reconnect are done by the holdings refresh runner, so **`Portfolio:AutoRefresh:Enabled = false` also switches the healing off** (the scheduled runs, "Sync now" and the "Retry now" button keep working) - and with it the once-a-minute reconcile of the attention items and the retention above, because both are done by the same tick: the items are then refreshed only at the end of a holdings, trades or ledger attempt and after a Dhan sign-in, and no resolved item is deleted. The 1-minute look of the healer, the 24 hours a resolved item is kept in the bell and the 2 hours before a login runs out at which it is called "expiring" are fixed in the code. Details: [doc 12](12-portfolio-monitoring.md) §3 "Self-healing and attention items (P2-16)".

### Live feed (`Live:*`, [ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md), P2-10)

`appsettings.json` of `xd-feed` (and `Live:ManualRunMinutes` of `xd-api` too); bounds are checked at start-up. The window itself (09:00–15:35, trading days) is the schedule of the service `live-feed` on the Services screen, not a setting here.

```yaml
Live:
  MaxWsInstruments: 200          # 1–5000: the cap of the active set (alerts, then indices, then holdings by value)
  FlushSeconds: 2                # 1–30: how often the latest ticks are written
  SilenceSeconds: 30             # 5–300: no packet for this long in market hours = dead connection (when an index is subscribed)
  BackoffInitialSeconds: 1       # 1–60: reconnect back-off 1, 2, 4 … BackoffMaxSeconds, jittered
  BackoffMaxSeconds: 30          # 1–300
  SubscribeSpacingMilliseconds: 250   # 0–5000: gap between subscription messages (≤ 100 instruments each)
  ManualRunMinutes: 30           # 1–240: how long Run now keeps the connection open outside the window
```

## 4. Most-used settings

| Setting | Meaning | Default |
|---------|---------|---------|
| `risk_profiles.*.max_new_entries_per_day` | Max new trades/day per account | 3 |
| `risk_profiles.*.max_open_positions` | Max open trades per account | 5 |
| `risk_profiles.*.risk_per_trade_pct` | Size of 1R | 0.5% |
| `styles.swing.max_holding_sessions` | Max holding | 5 |
| `allocation.mode` | Replicate or distribute across accounts | replicate |
| `scan_scope` | Which baskets are scanned, with which styles/sides | Best stocks, MyChoice, MyLongTerm |
| `data.csv_import.default_profile` | Column mapping for your CSV files | my_1min_files |
| `live.max_ws_instruments` | Cap on live-streamed instruments (active set) | 200 |
| `portfolio.min_days_for_xirr` | Below this, show absolute return instead of XIRR (v1: fixed at 30 in `Xirr.MinDays`, not yet configurable) | 30 |
| `selection.min_p_win` / `min_exp_R_lower` | Trade quality bar | 0.45 / +0.10R |
| `learning.min_trades` | Evidence needed per cell | 30 |
| `goals.max_drawdown_pct` | Halt-and-review threshold | 8% |
