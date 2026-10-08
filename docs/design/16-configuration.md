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
accounts:
  - id: dhan_main
    broker: dhan
    credentials: dhan_main               # Keychain item name — never the secret itself
    roles:
      data:      { enabled: true, primary: true }
      trading:   { enabled: true, mode: paper, profile: standard, styles: [intraday, swing],
                   instruments: { intraday: [EQ_MIS], swing_buy: [EQ_CNC], swing_sell: [FUT] },
                   capital: { source: broker_funds, cap: 1000000 } }
      portfolio: { enabled: true, default_bucket: Trading }
  - id: dhan_longterm
    broker: dhan
    credentials: dhan_longterm
    roles:
      portfolio: { enabled: true, default_bucket: MyLongTerm }
  - id: dhan_swing
    broker: dhan
    credentials: dhan_swing
    roles:
      data:      { enabled: true, standby: true }   # failover if primary token/subscription fails
      trading:   { enabled: true, mode: manual_live, profile: conservative, styles: [swing],
                   capital: { source: fixed, amount: 300000 } }
      portfolio: { enabled: true, default_bucket: Trading }

data_provider: { reconcile_with: nse_files }    # market data always from the primary Data account

access:
  idle_timeout_min: 30
  absolute_timeout_h: 12
  lockout: { attempts: 5, minutes: 15 }
  step_up_for: [account_roles, credentials, config_activation, execution, data_deletion]

approvals:
  plan_review_cutoff: "09:05"
  unreviewed_policy: lapse                # lapse | arm_top_n
  allow_modifications: [reduce_size, tighten_stop, change_account, swap_with_reserve]
  suggestions: { expiry_days: 3, snooze_after_reject_days: 14 }

risk_profiles:                     # shared; edited on Accounts → Risk profiles; accounts may override values
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
  adjust: { splits_bonus: true, dividends: false }
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
  sync: { time: "16:30", trade_history_backfill_from: 2023-01-01 }
  lot_method: fifo
  include_charges_in_cost: true
  default_bucket_for_plan_trades: Trading
  buckets: [Trading, MyLongTerm, MyChoice]
  benchmark: NIFTY 50
  min_days_for_xirr: 30
  alerts: { max_position_weight_pct: 15, bucket_drawdown_pct: 10, results_within_days: 3, day_move_pct: 5 }

timeframes:
  enabled: [5m, 10m, 15m, 30m, 1h, 1D]
  bias_map: { 5m: [15m, 1h], 10m: [30m, 1h], 15m: [1h, 1D], 30m: [1h, 1D], 1h: [1D], 1D: [1W] }

styles:
  intraday: { enabled: true, first_entry_time: "09:30", last_entry_time: "14:30", square_off_time: "15:15" }
  swing:    { enabled: true, max_holding_sessions: 5, signal_valid_sessions: 1 }

strategies:                        # built-ins enabled by id; your own strategies live in the Strategy Lab library (database)
  enabled: [ORB, VWAP_PULLBACK, EMA_PULLBACK, PDH_PDL_BREAK, BASE_BREAKOUT, TREND_PULLBACK, RSI2_REVERSION, NR7_ID_BO]

exits:
  candidate_policies:
    - { id: FIXED_RR, rr: [1.5, 2.0, 2.5, 3.0] }
    - { id: PARTIAL_BE, book_pct: 50, t1_rr: 1.0, t2_rr: [2.0, 3.0] }
    - { id: ATR_TRAIL, atr_period: 14, mult: [2.0, 3.0], activate_at_rr: 1.0 }
    - { id: STRUCTURE_TRAIL, lookback_bars: 3 }
    - { id: PYRAMID, initial_pct: 50, add_at_rr: [1.0, 2.0], add_pct: [30, 20], max_adds: 2,
        cap_total_risk_to_initial: true, trail: ATR_TRAIL }
  overlays:
    time_stop: { enabled: true, min_progress_rr: 0.5, intraday_bars: 6, swing_sessions: 2 }
    event_exit: { enabled: true, days_before_results: 1 }
  selection_objective: { metric: mean_R_minus_downside, lambda: 0.5, prefer_plateau: true }
  per_trade_fit: { scale_by_atr: true, snap_to_structure: true, obstacle_check: true,
                   late_entry_target_reduction: { after: "13:30", factor: 0.7 } }

costs:                             # fill from your broker's current charge sheet
  brokerage: { intraday_pct: 0.03, intraday_max: 20, delivery: 0 }
  stt_ctt: { eq_intraday_sell_pct: null, eq_delivery_pct: null, fut_sell_pct: null }
  exchange_txn_pct: null
  gst_pct: 18
  stamp_duty_buy_pct: null
  sebi_fee_per_cr: null
  slippage: { ticks: 1, bar_range_frac: 0.05, stop_exit_extra_ticks: 2, gap_fill: at_open }

learning:
  walk_forward: { train_years: 3, test_months: 3, embargo_sessions: 5 }
  min_trades: 30
  recency_half_life_days: 180
  bayes_prior: { type: beta, shrink_to_parent: true, strength: 20 }
  regime: { trend_inputs: [dma20_slope, adx14], vol_inputs: [vix_pct, atr_pct] }
  meta_model: { enabled: true, trainer: lightgbm, fallback_trainer: fasttree, split_by: [style, side],
                calibration: pav, must_beat_baseline: true }
  qualification_gates: { min_oos_trades: 30, min_expectancy_R: 0.15, min_profit_factor: 1.2,
                         min_positive_folds_pct: 60, max_drawdown_R: 10, max_calibration_error_pp: 5 }
  paper_days_before_active: 20
  drift: { window_trades: 20, below_band_checks: 3, action: pause }
  hypothesis_loop: { enabled: true, trigger: { schedule: weekly, min_new_trades_per_cell: 30, on_drift: true },
                     one_variable_only: true, max_open_hypotheses: 3, require_approval: per_autonomy }
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
  plugins: { enabled: true, sandbox: { cpu: 2, memory_gb: 4, timeout_min: 60 } }
  multiple_testing_adjustment: true
  assistant_can_run_tests: true
  assistant_can_promote: false

assistant:
  enabled: true
  model: "qwen3-30b-a3b"           # served by xd-llm
  context_tokens: 32768
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
Passwords and broker keys are read from `/run/secrets/*` (Podman secrets) via the KeyPerFile provider.

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
| `portfolio.min_days_for_xirr` | Below this, show absolute return instead of XIRR | 30 |
| `selection.min_p_win` / `min_exp_R_lower` | Trade quality bar | 0.45 / +0.10R |
| `learning.min_trades` | Evidence needed per cell | 30 |
| `goals.max_drawdown_pct` | Halt-and-review threshold | 8% |
