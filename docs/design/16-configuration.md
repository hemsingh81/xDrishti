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

costs:                             # the broker's charge sheet: effective-dated schedules, every rate present (a zero is written 0, never null); since P14-07 (ADR 0194) a schedule may also carry optional `options` and `futures` objects of the same ten fields, left out when the owner has none (an option leg is then gross and labelled, never charged zero); `shortcut:` the stored sheet text is bounded at 8,000 characters, so a sheet whose schedules carry both new segments holds about seven schedules, not twelve); built in P3-03c (ADR 0042); stored since P3-03f (ADR 0044) as one row of `platform.app_settings`, key `research.cost_sheet.<id>` (a new set of rates is a new id), loaded with `scripts/cost-sheet.sh load <file.json>` (`show`, `check` against your original report files; `--replace` overwrites an id), and chosen by every run: `--gross` or `--costs <id> [--capital] [--profile] [--max-cost-r]`; a report or the paper comparison prints figures as net only while **you have confirmed that stored sheet at its current rates** (Settings, tab Charge sheet; ADR 0127: a fresh password per act, switched off until you switch it on, a change of the sheet's rates makes the confirmation stale)
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
            volatility: { atr_period: 14, percentile_window: 252, low_below: 33.333333333333336, high_from: 66.66666666666667 } }  # exactly one third and two thirds (100/3, 200/3: the doubles of ContextOptions, never 33.33 / 66.67) of the mean of the VIX and index-ATR% percentiles; below low_below is LOW, from high_from is HIGH; confirmed 2026-10-27, ADR 0033 Amendments 1, 2 and 13 (the owner's wording was 33.3 and 66.7); breadth: { sma_sessions: 20, min_voters: 50, min_share: 0.5, grace_sessions: 5 } (P3-01e, `Data:Context:Breadth:*` and `Data:Context:BreadthGraceSessions`; the percent 0 to 100 of the context is divided by 100 once, at the plan context, because `breadth_min` is a fraction); max_age_days: 7 (ADR 0033 Amendment 14) is the bound on the age of a context read
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
  cells-refresh:    { schedule: { type: after, after: eod } }   # built in LC-01 (ADR 0277): starts paused, resuming needs the owner's password; recomputes the cell statistics as of the last closed session from the newest snapshot's sources, to the hold-out cut less the embargo, fails closed; bell notice at Learning:CellsRefresh:NoticeAfterSessions (Internal, default 3, 1..4)
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
  # CF-01 (ADR 0203) reads the configuration section Confidence: EffectiveN:Mode (Off | ReportOnly | Gate, default ReportOnly; Gate is the owner's switch), EffectiveN:MinSessionsForAutocorr 20 (5..250), EffectiveN:MinTradesForEstimate 5 (3..50), EffectiveN:MinClusters 5 (3..50), Power:Alpha 0.05 (0.001..0.2), Power:Power 0.80 (0.5..0.99), Power:SigmaFloorR 0.5 (0.1..5), Power:TargetEdgeR (unset = the gate's MinExpectancyR; 0.01..5), MinTrl:Confidence 0.95 (0.5..0.999), MinTrl:BenchmarkSr 0 (-1..1); a value out of range stops the host at start
  # CF-02 (ADR 0204) reads Confidence:Shrinkage: Mode (Off | ReportOnly, default ReportOnly; Gate is refused until the lower bound is validated), TauSquaredFloor 0.01 (0.0001..1), MinTradesForSibling 5 (3..30), MinParentTrades 30 (10..200), StrengthMin 5 and StrengthMax 200 (1..1000), QuantileLevel 0.05 (0.01..0.25), SigmaPoolStrength 30 (1..1000); the existing bayes_prior strength stays the fallback
  # CF-03 (ADR 0205) reads Confidence:Deflation: Mode (Off | ReportOnly, default ReportOnly; Gate is refused), MinProbability 0.95 (0.5..0.999), AssumedTrialSrSd (unset; 0.001..1), TBasis (NEff | Raw, default NEff); and Confidence:Bootstrap: Mode (Off | ReportOnly), Draws (unset = Robustness McDraws; 100..20000), BlockLength 0 (auto; else 2..60)
  # CF-05 ([ADR 0206](../adr/0206-the-lab-verdict-shows-a-cost-stress-at-1x-1-5x-and-2x-and-a-fill-one-minute-later-test-beside-the-outcome-in-report-only-mode-and-enforce-is-the-owners-switch.md)) reads Confidence:CostStress: Mode (Off | ReportOnly, default ReportOnly), Enforce (default false: the owner's switch), MidMultiplier 1.5 (>1..5), MidFloorR 0.08 (-1..1), HighMultiplier 2 (>Mid..5), HighFloorR 0 (-1..1), DelayMinutes 1 (1..5), DelayRetention 0.70 (0.1..1), and (S2, [ADR 0223](../adr/0223-the-lab-verdict-counts-each-trades-liquidity-tier-and-capacity-breach-from-day-bars-report-only-and-readiness-row-14-reads-them-with-unavailable-as-a-fail.md)) TierACutCrore 100 (100..100000), TierBCutCrore 20 (at least 20, below the A cut), CapacityFillShare 0.01 and CapacityDailyShare 0.005 (above 0, at most the default): tighten-only, the loose side is refused at start-up; the text and hash of the values are stored with every verdict
  # CF-06 ([ADR 0207](../adr/0207-a-strategy-version-is-pre-registered-the-ledger-counts-every-extra-look-and-a-locked-hold-out-is-fixed-at-the-first-post-and-read-once-by-an-owner-unlock-with-enforcement-and-the-honest-count-as-the-owners-switches.md)) reads Confidence:Registration: Enforce (default false), CountInFactor (default false); Confidence:HoldOut: Enforce (true since HO-01b, ADR 0282), AutoUnlock (true, ADR 0282; never claims block 1), AutoRelease (true, HO-01, ADR 0283), BlockMonths 6 (3..12, HO-01), MinMonths 6 (1..60), SharePercent 20 (5..50), MinTrades 30 (5..1000), Retention 0.5 (0.1..1); the text and hash of the HoldOut values are stored with every unlock
  # CF-07 ([ADR 0208](../adr/0208-the-lab-verdict-shows-a-random-entry-null-beside-the-sign-flip-in-report-only-mode-and-the-null-is-one-look-in-the-ledger-with-enforce-the-owners-switch.md)) reads Confidence:Nulls: Mode (Off | ReportOnly, default ReportOnly), Enforce (default false: the owner's switch), Draws 1000 (200..5000), Percentile 95 (90..99), Redraws 5 (0..20), Placebos (default true) and MinPlacebos 19 (19..20; CF-07 part B, [ADR 0209](../adr/0209-the-lab-verdict-shows-a-placebo-set-and-a-mirror-gate-beside-the-random-entry-null-in-report-only-mode-and-the-placebo-set-is-a-second-null-look-in-the-ledger.md)); the text and hash of the values are stored with every verdict CF-07 part C ([ADR 0210](../adr/0210-the-lab-verdict-shows-a-plateau-gate-on-the-swept-parameters-read-from-the-stored-in-sample-sweep-in-report-only-mode-with-its-own-enforce-switch-and-no-new-look.md)) reads Confidence:Plateau: Mode (Off | ReportOnly, default ReportOnly), Enforce (default false: its own owner switch), Retention 0.70 (0.50..0.95), MinPoints 3 (3..5).
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
# As built (P12-09, ADR 0167): auto_live keys exec.auto_live_allow_auto_approved false, exec.auto_live_require_day_unlock true, exec.auto_live_require_attended true,
# exec.auto_live_attended_min 10 (1..30); the per-account limits sheet is a record (execution.limits), empty by default, and an empty sheet allows nothing.
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

### Ticket alerts (`Planning:TicketAlerts:*`, [ADR 0115](../adr/0115-crossing-alerts-on-armed-tickets-are-an-observer-in-the-feed-flush-prompts-are-stored-questions-with-a-derived-status-and-the-bell-reads-them-through-the-ticket-notice-port.md), P7-20)

Read at start by `xd-feed` (the observer exists only when enabled) and `xd-api` (the notice source, the plan-screen read and the service); bounds are checked at start-up. No setting lets an order be placed.

```yaml
Planning:
  TicketAlerts:
    Enabled: false          # OFF: no observer, no watched list read, no row written by the feed, the bell source answers nothing, the service stores nothing
    PromptTradingDays: 3    # 1-10: trading days after the session a prompt stays open
    WatchedCacheSeconds: 30 # 5-120: how long the feed keeps its list of armed tickets with levels
```

**`xd-api` and `xd-feed` must have the same `Enabled` value** (compose passes one variable, `XD_TICKET_ALERTS`, to both): with the feed ON and the API OFF crossings are stored but nothing shows; with the feed OFF and the API ON nothing is stored.

The service `post-trade-prompts` (daily 16:45, trading days, starts paused) is resumed on the Services screen after the owner has read one session's log.

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

### Depth recorder (`Depth:*`, [ADR 0135](../adr/0135-the-depth-recorder-stores-the-20-level-book-of-up-to-50-shares-about-once-a-second-behind-three-gates-a-disk-guard-and-no-reader.md), P7-16)

`appsettings.json` / environment of `xd-feed` (`Depth__Enabled`; compose passes `XD_DEPTH_ENABLED`); bounds are checked at start-up. **Off by default, three gates:** this deployment flag (restart to change), the owner's stored switch (`PUT /api/depth/recorder`, the password typed again for every change, ON and OFF) and the service `depth-recorder` (starts paused; window 09:14–15:31 on trading days, edited on the Services screen). Only 09:15:00–15:30:00 is stored.

```yaml
Depth:
  Enabled: false          # the deployment gate; false = nothing is read, built or opened
  MaxInstruments: 50      # 1–50: Dhan allows 50 on one 20-level connection
  MaxArmed: 20            # 0–50: how many of them are today's armed tickets (rank order), then PinnedSymbols, then the most liquid
  PinnedSymbols: []       # canonical symbols always recorded, e.g. ["NSE:RELIANCE"]
  SampleMs: 1000          # 500–5000: at most one snapshot per instrument per this long, only when the book changed
  FlushSeconds: 5         # 1–30: how often the queue is written (COPY); rows unwritten for 30 s are dropped, oldest first
  MaxStoredGb: 40         # 1–500: the stored size above which recording stops (the app never deletes data)
  MinFreeGb: 20           # 5–500: free disk space below which recording stops
```

### Trade-tick recorder (`Ticks:*`, [ADR 0173](../adr/0173-the-trade-tick-recorder-stores-last-trade-updates-of-the-depth-set-when-cumulative-volume-rises-on-a-second-quote-socket-behind-three-gates-its-own-cap-and-no-reader.md), P7-16b)

`appsettings.json` / environment of `xd-feed` (`Ticks__Enabled`; compose passes `XD_TICKS_ENABLED`); bounds are checked at start-up. **Off by default, three gates:** this deployment flag (restart to change), the owner's stored switch (`PUT /api/ticks/recorder`, the password typed again for every change, ON and OFF) and the service `tick-recorder` (starts paused; window 09:14–15:31 on trading days). The day's set is the depth recorder's (or built the same way from `Depth:*` when none is stored by 09:14:30); the free-space floor is the shared `Depth:MinFreeGb`. Only 09:15:00–15:30:00 is stored, a row only when the cumulative volume rose.

```yaml
Ticks:
  Enabled: false          # the deployment gate; false = nothing is read, built or opened
  FlushSeconds: 5         # 1–30: how often the queue is written (COPY); rows unwritten for 30 s are dropped, oldest first
  MaxStoredGb: 12         # 1–500: the stored size of market.trade_ticks above which recording stops (the app never deletes data)
```

### Option-chain recorder (`Options:*`, [ADR 0176](../adr/0176-the-option-chain-recorder-stores-five-minute-index-chains-and-a-daily-stock-pass-from-two-new-allow-listed-data-api-endpoints-behind-three-gates-its-own-cap-and-no-reader.md), P14-02)

`appsettings.json` / environment of `xd-feed` (`Options__Chain__Enabled`; compose passes `XD_OPTIONS_CHAIN_ENABLED`); bounds are checked at start-up. **Off by default, three gates:** this deployment flag (restart to change), the owner's stored switch (`PUT /api/options/chain-recorder`, the password typed again for every change, ON and OFF) and the service `option-chain-recorder` (starts paused; window 09:14-15:31 on trading days; "Run now" refused). The underlyings are the app setting `options.underlyings` (doc 06 §2.1); the indices asked each slot are those of `IndexExpiries`, the other listed underlyings are the daily stock pass. The recorder sends at most one request per 3 seconds (Dhan: "one unique request every 3 seconds", UNVERIFIED whether per user); a larger `SpacingMs` is allowed, a smaller one is refused at start. A bound `IndexExpiries` list replaces the default.

```yaml
Options:
  MinFreeGb: 20           # 5-500: free disk space below which an options recorder stops (the app never deletes data)
  Chain:
    Enabled: false        # the deployment gate; false = nothing is read, built or sent
    IntervalMinutes: 5    # 5-60: the grid of slots from 09:20 to 15:30 (75 at 5)
    SpacingMs: 3000       # 3000-60000: the gap between any two requests of the process; never below 3000
    StrikesEachSide: 30   # 5-50: strikes kept either side of the one nearest the underlying (indices)
    StockStrikesEachSide: 15  # 3-30: the same for the stock chains
    IndexExpiries:        # empty = the default below; Weekly = the earliest expiries, Monthly = the earliest month-end ones not among them
      - { Symbol: "NSE:NIFTY", Weekly: 3, Monthly: 1 }
      - { Symbol: "NSE:BANKNIFTY", Weekly: 0, Monthly: 2 }
    StockExpiries: 2      # 1-3: expiries asked of each share, once a day from 14:30 to 15:25
    TimeoutSeconds: 10    # 1-15: a request that takes longer is a timeout gap, never retried
    MaxStoredGb: 4        # 1-100: the stored size of options.chain_snapshots above which recording stops
```

### Expired-options backfill (`Options:Backfill:*`, [ADR 0177](../adr/0177-the-expired-options-backfill-fetches-rolling-atm-offset-bars-from-one-new-allow-listed-data-api-endpoint-at-night-behind-four-gates-after-an-owner-run-probe-its-own-cap-and-no-reader.md), P14-03)

`appsettings.json` / environment of `xd-feed` (`Options__Backfill__Enabled`; compose passes `XD_OPTIONS_BACKFILL_ENABLED`); bounds are checked at start-up. **Off by default, four gates:** this deployment flag (restart to change), the owner's stored switch (`PUT /api/options/backfill`, the password typed again for every change, ON and OFF), the owner's acceptance of the probe result (`PUT /api/options/backfill/probe-acceptance`, password; the probe is `scripts/xd-probe-backfill.sh`, at most five requests, nothing stored) and the service `options-backfill` (starts paused; "Run now" refused). It runs only between 16:30 and 08:30 India time, every day. `Options:MinFreeGb` (above) is the shared free-space floor.

```yaml
Options:
  Backfill:
    Enabled: false        # the deployment gate; false = nothing is read, built or sent
    RequestsPerSecond: 1  # 0.2-2: requests a second for the whole process (Dhan's Data limit is 5 a second, shared with the day's calls)
    MaxRequestsPerDay: 40000  # 1000-90000: per India day, counted from the fetch log
    Years: 5              # 1-5: windows of 30 days back from the first of the month
    StockCount: 20        # 0-30: the best-ranked shares of options.underlyings (by turnover), MONTH 1 at 15 minutes
    IndexOffsets: 10      # 0-10: NIFTY and BANKNIFTY at ATM-n..ATM+n, 5 minutes (set from the probe)
    StockOffsets: 3       # 0-3: shares at ATM-n..ATM+n
    TimeoutSeconds: 15    # 5-60: a request that takes longer is a timeout, tried again up to three times
    MaxStoredGb: 3        # 1-50: the stored size of options.rolling_bars above which fetching stops (the app never deletes data)
```

### Option archive quality (`Options:Quality:*`, [ADR 0178](../adr/0178-the-option-archive-quality-checks-read-the-recorded-chains-and-rolling-bars-in-bounded-ranges-and-store-insert-only-verdicts-with-an-honest-unavailable-and-no-trading-use.md), P14-04)

`appsettings.json` / environment of `xd-api` (`Options__Quality__Rate`); bounds are checked at start-up. The service `options-quality` is **on by default** (it only reads the archive and writes its own result rows); these are the numbers it judges by and the bounds of one run.

```yaml
Options:
  Quality:
    Rate: 0.065               # 0-0.2: yearly rate of the put-call parity check (UNVERIFIED; set it if you want another)
    MaxIv: 300                # 1-1000: IV (percent assumed) at or above which a value is a violation
    OiJumpPct: 50             # 1-1000: an open-interest change above this percent (and above OiJumpMinLots lots) is counted
    OiJumpMinLots: 10         # 0-1000
    OiWarnShare: 0.01         # 0-0.5: jumps over this share of pairs warn (never fail)
    CoveragePass: 0.95        # 0.5-1: coverage at or above passes
    CoverageWarn: 0.80        # 0.1-1: at or above warns, below fails
    ParityPass: 0.90          # 0.5-1
    ParityWarn: 0.70          # 0.1-1
    GreeksWarn: 0.02          # 0-0.5: violations over this share of points warn
    GreeksFail: 0.10          # 0-1: over this share fail
    TickPaise: 5              # 1-100: one tick in the parity tolerance
    LookbackDays: 10          # 1-60: days back in which a day with no chain session row is reported as not recorded
    MaxChainSessionsPerRun: 20    # 1-400
    MaxRollingWindowsPerRun: 5000  # 1-5000: ceiling of fetch windows checked in one run, oldest first (MaxRunMinutes is the real bound)
    MaxRunMinutes: 30         # 1-120: a run stops between two units of work after this
```

### Option costs and liquidity (`Options:Costs:*`, `Options:Liquidity:*`, [ADR 0194](../adr/0194-the-option-cost-model-takes-optional-option-and-futures-lines-on-the-owners-sheet-a-half-spread-from-usable-chain-sessions-else-his-labelled-assumption-else-unavailable-rupee-liquidity-filters-and-a-lot-feasibility-count-apart-from-the-unit.md), P14-07)

`appsettings.json` / environment of the host that builds or reads the model (`Options__Costs__MinUsableSessions`); bounds are checked at start-up. Nothing here sends anything or switches anything; the charge lines themselves are the owner's, entered with `cost-sheet load` (see `costs` above), and `xd options spread-model build [--underlying SYMBOL]` builds the measured half-spread model (exit 0 with the reason when there is nothing to build; 2 for a bad line).

```yaml
Options:
  Costs:
    MinUsableSessions: 60       # 20-250: usable chain sessions (coverage pass, parity pass or warn) an underlying needs before the half-spread is measured
    MinQuotesPerBucket: 200     # 50-5000: quotes a bucket needs before its own median is used (else the time-merged, then the moneyness-merged pool)
    AssumedHalfSpreadPct: {}    # per short symbol (NIFTY), 0.01-20: the OWNER's assumed half-spread in percent of the mid for the time before 60 usable sessions. Empty by default and never filled by the repository; without it the spread is unavailable (spread_unavailable)
  Liquidity:
    MinOiNotionalCr: 5          # 0-1000: open interest x spot, in crore of rupees, at least (UNVERIFIED starting value)
    MinBarTurnoverLakh: 25      # 0-10000: the entry bar's volume x spot, in lakh of rupees, at least (UNVERIFIED starting value)
```

### Option re-mark (`Options:Remark:*`, [ADR 0197](../adr/0197-the-option-expression-simulator-re-marks-each-stored-cash-trade-as-one-long-option-on-a-fixed-strike-read-from-the-offsets-with-an-expiry-calendar-of-labelled-derived-dates-a-counted-model-price-fallback-and-every-unpaired-trade-counted-by-reason.md), P14-08)

`appsettings.json` / environment of the host that runs `xd options remark` (`Options__Remark__MaxIvAgeMinutes`); bounds are checked at start-up. Research only: nothing here places or plans an order. The charge basis is not a setting: the re-mark follows the stored cash run (a charged run is re-marked net on the same stored sheet, a gross run gross).

```yaml
Options:
  Remark:
    MaxIvAgeMinutes: 60       # 5-390: the oldest vendor IV, in minutes before a bar, the Black-76 fallback may use for a bar the archive lacks (no such IV: the trade is strike_left_window)
```

### Option phase check (`Options:PhaseCheck:*`, [ADR 0202](../adr/0202-the-p14-phase-check-reads-stored-verdicts-for-the-current-versions-only-closes-a-family-by-a-net-drop-or-a-hold-out-confirmed-keep-counts-every-expression-dropped-as-met-and-finds-a-non-cash-expression-in-use-by-a-select-of-the-stored-cell-keys.md), P14-17)

`appsettings.json` / environment of the host that runs `xd options phase-check` (`Options__PhaseCheck__StaleAfterDays`); bounds are checked at start-up. Read only: nothing here changes a setting, a switch or a verdict, and nothing places an order.

```yaml
Options:
  PhaseCheck:
    StaleAfterDays: 90        # 7-730: a stored verdict is stale when the option archive has moved on from the fetch it read (its watermark) by more than this many days, measured against the newest finished fetch, not the clock (a stopped archive does not age a verdict); a stale verdict leaves its family open
```

### Pairs study (`Pairs:*`, [ADR 0196](../adr/0196-the-pairs-study-counts-every-candidate-in-the-ledger-before-any-test-walks-pairs-in-a-separate-pair-1-walker-on-the-simulators-fills-and-judges-them-by-protocol-s-where-a-gross-drop-is-valid-and-a-gross-keep-impossible.md), P14-13)

`appsettings.json` / environment of the host that runs `xd pairs run` or `xd pairs eval` (`Pairs__PairNotional`); bounds are checked at start-up. Research only: nothing here places or plans an order. A run and an evaluation must use the same settings (the stored trades carry a hash of the notional, the charge basis and the fill rules; trades of other settings are counted and not judged).

```yaml
Pairs:
  PairNotional: 100000      # 10,000-10,000,000: the Y-leg notional in rupees; the X leg is beta times it; whole shares
  MaxCandidates: 2000       # 100-25,000: the most correlated candidate pairs one formation keeps (every candidate is a trial in the test ledger)
  CostSheetId: ""           # a stored cost sheet (cost-sheet load) with the intraday equity lines; empty: gross, every trade labelled gross (a gross KEEP is impossible, a gross DROP is valid)
```

### Filings inbox (`Filings:*`, [ADR 0179](../adr/0179-filings-come-from-a-local-inbox-folder-with-a-sidecar-that-carries-the-broadcast-time-the-adapter-is-read-only-and-the-public-page-fetcher-is-a-recorded-decision-not-to-build.md), P15-01)

`appsettings.json` / environment of the host that reads the folder (`Filings__InboxPath`); bounds are checked at start-up. The inbox is created `0700` by `scripts/filings-drop.sh`; the source refuses a folder open to group or others and never changes a mode. **There is no setting that fetches anything:** the key `filings.fetch.enabled` is only *reserved* (not built, default false) for a later slice that needs the owner's reading of the exchange terms first.

```yaml
Filings:
  InboxPath: data/filings/inbox   # relative = against the host's working directory; a host sets Filings__InboxPath explicitly (container: /data/filings/inbox)
  MaxFileMb: 20                   # 1-200: a larger file is rejected too_large, unread
  MaxFiles: 5000                  # 100-50000: files examined per scan; more is reported truncated
  FilesPath: data/filings/files   # P15-02: the store folder of the ingested files (0700/0600); container: /data/filings/files, set explicitly
  LagMinutes: 5                   # P15-02, 0-60: minutes after the broadcast until a filing counts as known (deployment setting, never owner-switchable)
  MaxFilesGb: 5                   # 1-100: cap on the store folder; reaching it stops the ingest (disk_budget), nothing is deleted
  MinFreeGb: 20                   # 5-500: free space the volume keeps; below it the ingest stops (disk_free)
```

The ingest is the service `filings-ingest` of the scheduler in `xd-api` ([ADR 0180](../adr/0180-the-point-in-time-filing-store-keeps-immutable-envelopes-and-answers-what-was-known-at-an-instant-from-the-broadcast-time-plus-a-lag-and-nothing-else.md), P15-02): registered **paused**, daily 17:05 on trading days, "Run now" allowed (local files only). Compose: `XD_FILINGS_LAG_MINUTES`, `XD_FILINGS_MAX_FILES_GB`, `XD_FILINGS_MIN_FREE_GB`.

`exclude.results_within_days` (and the same word in the alerts and the pre-trade checks) is read **in trading sessions** counted to the event session of the next results meeting as known at the decision instant ([ADR 0181](../adr/0181-the-results-calendar-keeps-each-meeting-row-with-its-own-intimation-time-counts-days-in-trading-sessions-to-an-event-session-and-answers-unavailable-for-an-instrument-with-no-entry.md), P15-04): a Saturday or holiday meeting counts on the next session, a meeting published after the instant is not known, and an instrument with no entry is `unavailable`, not "far away". The same `filings-ingest` run loads the board-meeting and shareholding csv files (counts per file and per run, no new setting). P15-06 ([ADR 0182](../adr/0182-the-event-risk-filter-reads-the-results-calendar-in-the-simulator-and-behind-an-owner-switch-in-the-plan-and-the-results-loss-tag-describes-the-past-and-unavailable-is-never-a-pass.md)) wires the number in behind `smart_rules.event_filter.enabled` (default OFF; the owner changes it, and `results_within_days` (default 2, 0 to 10) and `approval` (`manual` default, or `auto`), only with `PUT /api/selection/event-filter` and a fresh password; every change is a row of `filings.event_filter_decisions`, the newest row wins, no row or an unreadable table is OFF). ON: the plan selection excludes a candidate with `0 <= days <= N` and holds one with no entry (unless the owner listed `event` as optional); the plan check "results within N days" gets its days (its limit 3 is unchanged); the execution check stays `unavailable`. With `approval = auto` the system may turn the filter on only from a stored protocol-F KEEP verdict for the current rule version, records it as `auto` and never as an owner decision, and turns an automatically enabled filter off when the verdict stops being KEEP; until P15-10 supplies a verdict it cannot fire.

### History backfill (`Data:History:*`, CF-04, [ADR 0270](../adr/0270-the-dhan-one-minute-backfill-writes-only-before-each-instruments-first-csv-bar-through-a-resumable-chunk-ledger-and-pre-seam-bars-stay-out-of-every-run.md), [ADR 0273](../adr/0273-pre-seam-bars-enter-a-run-only-for-an-instrument-proven-verified-through-a-stored-basis-with-the-switch-off-by-default-and-the-seam-by-date.md))

Deployment settings (`Data__History__...`), bounds checked at start-up. Every fetch and every use of backfilled bars is **OFF by default**; turning one on is the owner's act (NSE terms and allow-list for the NSE files).

| Option | Meaning | Default |
|--------|---------|---------|
| `Data:History:Nse:Enabled` | Daily bhavcopy and corporate-action fetches from NSE (Part A). Siblings: `Series` (EQ), `MaxPerDay` 2000, `DelayMs` 1500, `Years` 10, `UserAgent` (empty; set by the owner) | `false` |
| `Data:History:Backfill:Enabled` | Dhan 1-minute backfill before each instrument's first CSV bar (Part B). Shown read-only on Data > History | `false` |
| `Data:History:Backfill:Years` / `ChunkDays` / `MaxRetries` | Depth, chunk length and retries per chunk | `5` / `90` / `3` |
| `Data:History:Backfill:DiskHeadroomFactor` / `BytesPerRowEstimate` | Disk guard inputs (estimated bytes per row, headroom factor) | `2` / `60` |
| `Data:History:Backfill:RunWindow` | `HH:mm-HH:mm` India time in which the `history-backfill` service runs (every trigger) | `20:00-06:00` |
| `Data:History:Replay:AdmitVerified` | Lets pre-seam backfilled bars enter a run for an instrument proven verified (CF-04d); with it OFF pre-seam bars stay out of every run | `false` |

### System replay (`Replay:*`, SR-01, [ADR 0272](../adr/0272-the-system-replay-is-research-only-fenced-from-evidence-and-hindsight-labelled-and-runs-the-live-pieces-over-as-of-facts-in-memory.md))

Internal settings (AU-08 descriptors); a value out of range fails start-up.

| Option | Meaning | Default | Range |
|--------|---------|---------|-------|
| `Replay:IncompleteDataShare` | A session whose bars are missing for more than this share of the universe is `data_missing`: no new trades | `0.2` | 0 to 1 |
| `Replay:ReadAheadSessions` | Sessions of bars read ahead of the cut in one chunk of the rolling window | `40` | 1 to 250 |
| `Replay:MaxWindowMb` | The most memory the bars of one replay may need; a larger scope is refused (`replay.invalid`). 300 allows 53 instruments. Raise it together with the `mem_limit` of `xd-api` (1g today) | `300` | 64 to 4096 |
| `Replay:MinFreeGb` / `RunInMarketHours` / `PollInterval` | The job's yield rules: free disk floor, whether it runs 09:00 to 15:45 IST on trading days, queue look interval | `40` / `false` / 5 s | 10 to 500 |

### The Auto switch (`auto.enabled`, AU-17, [ADR 0252](../adr/0252-the-auto-switch-resumes-five-chain-services-follows-the-stored-switch-for-the-builder-and-restores-only-what-it-resumed.md))

A stored owner switch (`auto.enabled` in `platform.app_settings`; no row, or an unreadable one, is OFF). ON needs a fresh password and passing preconditions; OFF needs only the session. It resumes the services `eod`, `portfolio-broker-history`, `weekly`, `nightly` and `suggestions`, and places no order.

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
| `Confidence:EffectiveN:Mode` | How effective n is used: `Off`, `ReportOnly` (shown, decides nothing), `Gate` (the owner's switch: `Enough` and gate 1 compare effective n) (CF-01, ADR 0203) | ReportOnly |
| `Confidence:Shrinkage:Mode` | Whether the shrunk expectancy is computed and shown beside the raw one: `Off`, `ReportOnly` (shown, decides nothing). `Gate` is refused until the lower bound is validated (CF-02, ADR 0204) | ReportOnly |
| `Confidence:Deflation:Mode` | Whether the deflated Sharpe ratio is computed and shown beside the factor: `Off`, `ReportOnly` (shown, decides nothing). `Gate` is refused (CF-03, ADR 0205) | ReportOnly |
| `Confidence:Deflation:MinProbability` | The probability the deflated Sharpe ratio is read against, shown beside it | 0.95 |
| `Confidence:Deflation:AssumedTrialSrSd` | The owner's assumed spread of trial Sharpe ratios per trade when the stored statistics do not cover the family | unset |
| `Confidence:Deflation:TBasis` | T in the deflated Sharpe ratio: `NEff` (effective trades) or `Raw` | NEff |
| `Confidence:Bootstrap:Mode` | Whether the stationary block bootstrap of a robust run is computed: `Off`, `ReportOnly` | ReportOnly |
| `Confidence:Bootstrap:Draws` | Resamples of the block bootstrap; unset takes `Robustness` McDraws | unset |
| `Confidence:Bootstrap:BlockLength` | Mean block length in sessions; 0 is `round(k^(1/3))`, at least 2 | 0 |
| `Confidence:CostStress:Mode` | Whether the cost stress (1x, 1.5x, 2x) and the fill-one-minute-later test are computed and shown beside the outcome: `Off`, `ReportOnly` (CF-05, ADR 0206) | ReportOnly |
| `Confidence:CostStress:Enforce` | The owner's switch: on, a missed stress gate caps a PASS at MARGINAL; off, the figures decide nothing | false |
| `Confidence:CostStress:MidMultiplier` / `MidFloorR` | The middle multiplier of charges and slippage and the least net expectancy at it | 1.5 / 0.08 R |
| `Confidence:CostStress:HighMultiplier` / `HighFloorR` | The high multiplier and the net expectancy that must be exceeded at it | 2 / 0 R |
| `Confidence:CostStress:DelayMinutes` / `DelayRetention` | Minutes the entry fill is delayed and the least share of the base expectancy per signal that must remain | 1 / 0.70 |
| `Confidence:CostStress:TierACutCrore` / `TierBCutCrore` | The 20-day median traded value in crore rupees from which a trade is liquidity tier A and tier B; below the B cut is tier C (CF-05 D2, ADR 0223). Tighten-only: a value below the default is refused (raising puts names in lower tiers) | 100 / 20 |
| `Confidence:CostStress:CapacityFillShare` / `CapacityDailyShare` | The most an order may be of the fill minute's traded value (not read from day bars yet, so that cap reads unavailable) and of the 20-day median (CF-05 D4, ADR 0223). Tighten-only: a value above the default is refused | 0.01 / 0.005 |
| Lab post options `slippage` / `stress` (ADR 0229, request fields, not settings) | `slippage: conservative` prices the fill by the liquidity tier (tier A 3 bps, tier B 8 bps, range share 0.10 are the defaults of the tighten-only settings `Confidence:CostStress:TierABps` 3..5, `TierBBps` 8..15 and `RangeFraction` 0.10..0.20, judgment calls with no authoritative NSE source, to be replaced by measured paper fills (P11-02), ADR 0230; tier C and unavailable are excluded and counted); `stress` turns the cost stress block on or off for a post | `default` / on for `robust`, off for `quick` and `full` |
| `Confidence:CostStress:MinTurnoverCrore` (ADR 0230) | the filter `min_turnover` passes only when the 20-day median traded value (close x volume, sessions before the entry date) is at least this many crore; missing bars, under 20 sessions or missing volume block the entry and count it (`TurnoverBlocked`); tighten-only, 20..50, a judgment call to be replaced by measured paper fills | 20 |
| `Confidence:Registration:Enforce` | The owner's switch: on, a full or robust run of an unregistered version is refused (`lab.not_registered`); off, the report only warns | false |
| `Confidence:Registration:CountInFactor` | The owner's switch: on, the honest count of looks tightens the gates; off, the distinct-hash count judges and the honest count is shown beside it | false |
| `Confidence:HoldOut:Enforce` | The owner's switch: on, an explicit Lab window that reaches the hold-out is refused and a PASS without a kept hold-out is capped at MARGINAL; a default window is always cut | true (HO-01b, ADR 0282) |
| `Confidence:HoldOut:AutoUnlock` | The auto option: on, the one hold-out look runs by itself when every development row is a pass, recorded as `auto`; never at block 1 of the chain (HO-01) | true (ADR 0282) |
| `Confidence:HoldOut:AutoRelease` | HO-01: on, a sealed block with a fresh look and no look still available is released by itself, recorded `auto` with the setting snapshot; an unlooked block is never released automatically. A change in either direction needs a fresh password | true (ADR 0282, 0283) |
| `Confidence:HoldOut:BlockMonths` | HO-01: months of every hold-out block after the first, read when that block is stored (3..12) | 6 |
| `Confidence:HoldOut:MinMonths` / `SharePercent` | Shortest hold-out and its share of the stored history | 6 / 20 % |
| `Confidence:HoldOut:MinTrades` / `Retention` | Fewest hold-out trades and the share of the development expectancy that must remain | 30 / 0.5 |
| `Confidence:Nulls:Mode` | Whether the random-entry null of a robust run is computed and shown beside the outcome: `Off`, `ReportOnly` (CF-07, ADR 0208) | ReportOnly |
| `Confidence:Nulls:Enforce` | The owner's switch: on, a random-entry null, placebo set or mirror gate that is a fail or unavailable caps a PASS at MARGINAL; off, the figures decide nothing. `Enforce` with `Mode` Off stops the host at start | false |
| `Confidence:Nulls:Placebos` / `MinPlacebos` | Whether the placebo set and the mirror gate are computed beside the null (the 20 shapes are fixed), and the fewest comparable placebos for a conclusive set (19 or 20) | true / 19 |
| `Confidence:Nulls:Draws` / `Percentile` / `Redraws` | Seeds (each one full set of random-entry trades), the percentile that sets the level of the random-entry test (`1 - percentile / 100`; the placebo set's level is fixed at 0.05 whatever it is), and the times a draw that makes no trade is drawn again before it is dropped and counted | 1000 / 95 / 5 |
| `Confidence:Plateau:Mode` / `Enforce` | The plateau gate on the swept parameters of a robust run (CF-07 part C, [ADR 0210](../adr/0210-the-lab-verdict-shows-a-plateau-gate-on-the-swept-parameters-read-from-the-stored-in-sample-sweep-in-report-only-mode-with-its-own-enforce-switch-and-no-new-look.md)): `Off` or `ReportOnly`; `Enforce` is the owner's own switch for it (not `Nulls:Enforce`): on, a gate that is a fail or unavailable caps a PASS at MARGINAL. It is computed inside the nulls block, so `Enforce` with `Plateau:Mode` Off or `Nulls:Mode` Off stops the host at start | ReportOnly / false |
| `Confidence:Plateau:Retention` / `MinPoints` | The share of the base net expectancy that each of the two points next to the declared value must keep (0.50..0.95), and the fewest valid points of a parameter, the declared value included, for a reading (3..5) | 0.70 / 3 |
| `Confidence:Ladder:Mode` | Whether the complexity ladder, the events-per-variable cap and the feature stability checks are shown in a model's validation report: `Off`, `ReportOnly`; `Gate` is refused, nothing here enforces (CF-10 part 10b, [ADR 0215](../adr/0215-a-models-complexity-is-shown-against-a-ladder-with-an-events-per-variable-cap-and-its-features-against-a-stability-index-and-a-direction-check-as-evidence-only.md)) | ReportOnly |
| `Confidence:Ladder:Epv` / `MaxLogisticFeatures` / `MinTreeEvents` / `MinProbability` | Events per variable (5..50) and the most features of a logistic model (1..50); the fewest events for trees (10..100000); the probability a rung needs of beating the rung below (0.50..0.999, and not below `Confidence:PairedBootstrap:PromisingFrom`) | 15 / 8 / 100 / 0.95 |
| `Confidence:Ladder:PsiWatch` / `PsiAlarm` / `PsiBins` / `MinRows` / `MaxFlipShare` | The stability index cuts (0 < watch < alarm <= 5), its bins (2..100; fewer on small samples), the fewest rows for an index or a direction (5..100000), and the share of folds in which a feature's direction may flip (above 0, below 0.5) | 0.10 / 0.25 / 10 / 30 / 1/3 |
| `Confidence:PairedBootstrap:Mode` | Whether the paired day-cluster bootstrap and the fold sign test are shown beside the ladder in a model's validation report: `Off`, `ReportOnly`; `Gate` is refused, nothing here enforces (CF-10 part 10c-1, [ADR 0216](../adr/0216-the-probability-that-a-model-beats-the-rung-below-comes-from-a-paired-day-cluster-bootstrap-with-a-fold-sign-test-and-sample-uniqueness-as-evidence-only.md)) | ReportOnly |
| `Confidence:PairedBootstrap:Draws` / `Level` / `MinDays` / `PromisingFrom` | Resamples (100..20000), the interval's coverage (above 0.5, below 1), the fewest trade days for a figure (2..1000; fewer is "not available"), and the probability from which a result reads "promising" (0.50..0.99, not above `Confidence:Ladder:MinProbability`, the pass bar) | 2000 / 0.90 / 10 / 0.90 |
| `Confidence:MlInfluence:HoldOutMinRows` / `HoldOutMinProbability` / `ForwardMinCandidates` / `ForwardMinWeeks` / `ForwardMaxEcePp` | The evidence the `MlMayInfluence` predicate asks for (CF-10 part 10c-2, [ADR 0217](../adr/0217-a-model-may-influence-a-decision-only-through-one-gate-whose-predicate-needs-an-unspent-hold-out-forward-evidence-no-calibration-alarm-and-the-owners-switch.md)): hold-out rows (100..100000), the probability of beating the best no-ML baseline there (0.95..0.999, and not below `Confidence:Ladder:MinProbability`), graded forward shadow candidates (30..100000), weeks they span (4..520) and the largest rolling calibration error in percentage points (above 0, up to 5). Configuration can only tighten the spec's proposals until the owner-act settings exist (10d). No mode and no switch: the owner's switches `ml.influence.enabled`, `ml.auto_promote` and `ml.auto_rollback` are **stored state, not configuration** (CF-10 part 10d-1, [ADR 0220](../adr/0220-a-challenger-is-promoted-only-on-the-spec-rule-and-a-champion-is-rolled-back-by-the-p6-09-detectors-behind-three-owner-switches-that-are-all-off-by-default.md)): rows of `research.ml_switch_settings`, all OFF when there is no row, changed only by the owner through `PUT /api/learning/ml-settings` (a fresh password to turn influence or auto-promotion ON). The promotion rule reads the same 30 candidates, 4 weeks and 0.95 bars, so tightening them tightens promotion too | 100 / 0.95 / 30 / 4 / 5 |
| `Confidence:Decay:WindowTrades` / `Sigmas` / `QuarterFloorR` | The decay monitor of a strategy's forward series (CF-12 part 12a, [ADR 0218](../adr/0218-the-decay-monitor-the-kill-rule-evaluations-and-the-loop-report-are-read-only-evidence-with-every-input-that-has-no-source-yet-unavailable-and-no-consumer.md)): trades in one non-overlapping window (20..60), the standard errors below the reference mean at which a window alerts (above 0, up to 1.5) and the mean R below which a calendar quarter is weak (-0.05..0). Configuration can only tighten the spec's proposals. A section of configuration, not the versioned home of ADR 0002 (a stored version is a later part). Report only | 60 / 1.5 / -0.05 |
| `Confidence:Kill:StressMultiplier` / `DrawdownFactor` | The kill rules of a strategy's forward series (CF-12 part 12b, [ADR 0219](../adr/0219-a-firing-kill-rule-writes-a-proposal-and-a-notice-the-owner-or-an-automatic-setting-that-is-off-by-default-pauses-the-cells-and-only-the-owner-with-a-fresh-password-ever-re-enables.md)): the multiplier of cost in rule (a) (1.5..3, it can only be raised) and the factor on the 95th percentile of the matched drawdown in rule (b) (1..2, it can only be lowered), so a kill fires no later than the spec says. The trailing 100 trades and the 20 new trades between firings are fixed, not settings. **The automatic setting `confidence.kill.auto` is not configuration**: it is the owner's stored switch (`confidence.kill_settings`, no row means OFF; ON on a fresh database through the `system:default` seed row of ADR 0282 (AM-01a), which only the migration can write; changed only by `PUT /api/confidence/kill-settings` with a fresh password). Configuration, not the versioned home of ADR 0002 | 1.5 / 2 |
| `Confidence:Readiness:MinNEff` / `MinTradesPerCell` / `ExpectancyR` / `TStatistic` / `MinQuarterShare` | The thresholds of the edge checklist that the readiness scorecard judges itself (CF-11 part 1, [ADR 0221](../adr/0221-the-readiness-scorecard-is-derived-and-read-only-unavailable-is-a-fail-and-the-owners-enforce-switch-off-by-default-makes-the-two-lifecycle-gates-refuse-instead-of-warn.md)): rows 2, 3, 6 and 10. Each can only be **raised**, so a row fails no less often than the spec says (300..100000 effective trades, 30..10000 per cell, 0.15..2 R, t 2..10, 0.70..1). Stricter is true in both directions of use: the gates then refuse more, never fewer. The rows another slice judges (cost stress, the nulls, the plateau, the hold-out) keep that slice's settings | 300 / 30 / 0.15 / 2.0 / 0.70 |
| `Confidence:Readiness:MaxYearShare` / `MaxInstrumentShare` / `DrawdownBudgetR` | The ceilings of rows 10 and 11. Each can only be **lowered** (0.20..0.60, 0.02..0.15, 1..20 R) | 0.60 / 0.15 / 20 |
| `Confidence:Readiness:BuildingFrom` / `UsableFrom` / `ReadyFrom` | The cut-offs of the labels "Building", "Usable with advice" and "Ready for the next phase". Each can only be **raised** (40..99, 70..99.5, 85..100) and the three must increase, so a score never reads a higher label than the spec gives it. The sub-score anchors are not settings (a change is a code change with an ADR; the rules version is part of every snapshot's settings text and hash). **The enforce switch `confidence.readiness.enforce` is not configuration**: it is the owner's stored switch (`readiness.settings`, no row means OFF, ON only by `PUT /api/confidence/readiness-settings` with a fresh password, OFF too (it removes a refusal)). Configuration, not the versioned home of ADR 0002 | 40 / 70 / 85 |
| `Confidence:Loop:MinDays` / `Draws` / `Level` / `HelpsAt` | The three-arm loop test (CF-12 part 12a, [ADR 0218](../adr/0218-the-decay-monitor-the-kill-rule-evaluations-and-the-loop-report-are-read-only-evidence-with-every-input-that-has-no-source-yet-unavailable-and-no-consumer.md)): the fewest distinct trade days (20..250; fewer is inconclusive; a trade-off, not a tightening, since more days delay every verdict), bootstrap draws (2000..20000), the interval's coverage (0.90..0.99) and the probability at which an arm "helps" (0.95..0.999). "Hurts" is fixed at P(worse) of 0.95. Draws, Level and HelpsAt can only be tightened; report only | 20 / 2000 / 0.90 / 0.95 |
| `Confidence:Loop:MinSlopePer50Rows` / `TargetGain` | The learning curve: the gain per 50 rows below which a curve is "flat" (above 0, up to 0.1) and the gain the projection aims at (0.001..0.1) | 0.002 / 0.01 |
| `Confidence:Loop:SlopeLow` / `SlopeHigh` / `SlopeSe` | The reliability slope (trailing window fixed at 200 outcomes; with fewer the slope is shown and never alarms): the alarm bars (0.8 up to but not including 1; above 1 up to 1.2) and the standard errors from 1 the slope must also be (1..2). Tighten only | 0.8 / 1.2 / 2 |
| `goals.max_drawdown_pct` | Halt-and-review threshold | 8% |

## Visible settings (planned, AU-08 and AU-09, [ADR 0242](../adr/0242-few-visible-settings-and-learned-changes-tighten-automatically-while-loosening-needs-the-owners-password.md))

At most eight Essential settings are shown first; the rest stay under `Advanced settings` or are internal. Each has a validated range and a safer direction. New AU keys (`Recovery:*`, `Simulate:*`, `Training:*`, `Learned:WeeklyCap`) are not Essential.
