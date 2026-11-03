# P14-13 — Pairs simulation and evaluation: same-sector cash pairs formed on 252 daily closes, traded intraday on 5-minute spreads for the next quarter, both legs through the simulator's fills and costs, every candidate in the ledger before any result, and protocol S's verdict

- **Status:** Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-13 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Pairs simulation and evaluation"); protocol S items 1 to 4; phase acceptance criteria 5 and 6.
- **Design refs:** docs/design/20 §C (`PAIRS`), 07 §7 (swing SELL only through futures; intraday SELL allowed), 08 · P14-00 D10, D11, risk 6 · P13-05 (`CalendarFolds`, `BlockBootstrap`, ledger-first, records, hold-out once) · ADR 0037 (engine versions), 0038 (fills), 0042/0044 (costs), 0059 (ledger, `VerdictEvaluator`). New ADR: **next free number at build time** (the pair walker and its version `pair-1`, protocol S as built).
- **Needs:** P14-12 (the core), P13-05 (built), P5-03 (built), the simulator's fill and cost code (built). **Data:** today's 1-minute bars (2.1 years) are enough to **build and run** it; a verdict needs 6 evaluable quarters after a 252-session formation plus a 6-month hold-out, i.e. CF-04 Part B (5 years); until then NOT_EVALUABLE. **Owner acts (defaults if missing):** the intraday equity charge lines (ISS-25; default gross: KEEP impossible, DROP possible), an F&O ban list source (none exists; default `unavailable`, labelled).
- **Size:** 3.5 agent-days (estimate).
- **Safety:** no — research simulation over stored bars. Both legs are intraday cash (MIS in the plan's words) only inside the simulator; nothing selects, plans or orders a pair, and no two-leg order path exists or is built (P14-00 scope).

## Goal
The Lab can say, with every look counted, whether slow statistical arbitrage on same-sector cash pairs has an out-of-sample edge after costs for this account, and stores KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE with its evidence.

## Verified facts (read 2026-11-03 in `backend/src`)
- `market.instruments` has `Sector` (varchar 100, indexed; today's value, from the master refresh), the F&O flag of 213 underlyings, and an ASM/GSM flag (`InstrumentLimits.AsmGsmFlag`) as of today only; **no dated sector, no F&O ban list, no dated ASM/GSM history** exists.
- `market.bars_1m` (about 2.1 years, 214 instruments) and `market.bars_agg` (aggregates by timeframe) exist. The simulator walks one instrument per strategy; `SimulationEngine.Version` is `sim-9`; intraday SELL is supported, swing SELL refused.
- `CostModel.Leg(sheet, CostSegment.IntradayEquity, side, turnover, date)` charges one leg; no sheet with real rates is stored (ISS-25), so real runs are `--gross`.
- `ExtensionFamilies.TrialOf` builds a trial hash from family + text (not the data window), so re-running the same trial on new data adds no new hash.

## Scope
**In:**
- Domain (pure): `PairUniverse` (same-sector candidate pairs, the correlation filter, the cap), `PairFormation` (per formation window: tests, BH, half-life, the selected list), `PairTradeRule` (sizing, R unit, the session rules), `ProtocolS`, `PairFamilies` (`pairs:intraday-1`), `PairTrial` (candidate and variant trial texts).
- Simulation: `PairWalker` (two legs on 1-minute bars with the existing fill, slippage and cost code; version `pair-1`); single-instrument walks unchanged.
- Application: `RunPairsHandler` (verb `xd pairs run`), `EvaluatePairsHandler` (verb `xd pairs eval`), ledger appends before results.
- Migration `AddPairResearch`: `research.pair_formations`, `research.pair_trades`.
- The shared route accepts the pairs family (as P14-09).
- Tests, ADR, doc 20 §C.

**Out:** swing or futures pairs (≥ ₹15 lakh a leg, D10); live or paper use; a ban-list source; a dated sector history; Kalman hedge ratios; a screen (P14-16).

## The rules
- **Universe and candidates.** F&O shares with bars, grouped by today's `sector` (labelled `sector: as of today`, a survivorship-like limit). Candidates: every same-sector pair whose daily log-return correlation over the formation window is ≥ 0.8. If more than `MaxCandidates` (2,000), keep the 2,000 most correlated (ties by the symbols' ordinal order). `y` is the member with the larger 20-session mean turnover at the window end.
- **Formation** (each calendar quarter; window = the 252 sessions before the quarter's first session, daily closes = the last 1-minute close of each session): `HedgeRatio`, Engle-Granger (lags 1), half-life in [1, 20] sessions on the daily spread, Benjamini-Hochberg at q = 5 % **over all candidates of that formation** (an unavailable test counts with p = 1). Selected pairs keep β and the formation's spread mean for the quarter.
- **Trading** (the quarter's sessions, 09:15 to 15:15): 5-minute spread `ln y − α − β ln x` from 5-minute closes; z against the spread values of the previous `L` sessions (`L` ∈ {5, 10}); entry `|z| ≥ e` (`e` ∈ {1.5, 2.0, 2.5}), exit `|z| ≤ 0.5`, stop `|z| ≥ 3.5`, flat at 15:15; one position per pair; no entry after 14:45; **open only when `entry <= |z| < stop`** (P14-12's state machine would open at `|z| >= stop` and stop out next bar; ADR 0192). Decisions at a 5-minute close; each leg fills at its next 1-minute bar open by the simulator's fill rules and slippage; a leg with no bar there → the pair does not open (`leg_unfilled`, counted); an exit with a missing leg bar uses that leg's next bar (counted `leg_late`).
- **Sizing and R.** Y-leg notional `PairNotional` (₹1,00,000, setting); X-leg notional `β ×` that; whole shares (floor; a leg of 0 shares → `not_sizable`). **Rupee risk** = Y notional × `(3.5 − |z_entry|) × sd(W)` (the spread loss at the stop in log terms on the Y notional). `R` = net rupees of both legs ÷ rupee risk. Hand example: notional ₹1,00,000, entry `z` 2.0, `sd(W)` 0.004 → risk ₹600; a gross P&L of +₹450 before costs → 0.75 R.
- **Costs and gross.** Four fills per trade, each through `CostModel` (intraday equity) when the owner's sheet exists; else gross, labelled.
- **Lists.** F&O ban: no source → `ban_list: unavailable` printed as a limit (never read as "not banned"). ASM/GSM: today's flag only → excluded with the label `asm_gsm: as of today`.

## Protocol S as built (family `pairs:intraday-1`)
- **Trials and N.** Each candidate pair is one ledger trial (text: family, ordered symbols, test definition: lags, q, half-life range), appended **before** its formation statistics are computed; each of the 6 trading variants (`e` × `L`) is one trial. A pair re-formed in a later quarter has the same hash (no new look); a new pair adds one. N = distinct hashes; `F = Factor(N)` (2,000 candidates + 6 variants → 3.74; all 22,578 pairs of 213 shares would be 4.62). The primary pre-registered variant is `e` 2.0, `L` 5.
- **Folds.** Calendar quarters (= trading windows) via `CalendarFolds` (quarter unit, embargo 5 sessions — the formation window ends before the quarter by construction); evaluable when it has ≥ 20 pair trades; hold-out = the last 6 months, read once with `--holdout`.
- **Bounds.** `BlockBootstrap` of pair-trade R by day blocks with the 5-session sensitivity, 10,000 draws, seed 1337, one-sided 90 %.

| Verdict | When |
|---|---|
| NOT_EVALUABLE | evaluable quarters < 6; pooled OOS pair trades < 300; a bound unavailable; names each (`quarters 2 of 6`). |
| KEEP | all: ≥ 6 quarters; ≥ 300 trades; lower bound of mean R ≥ `0.05 R × F` at both block lengths; mean R > 0 in ≥ 2/3 of quarters; the Lab gates (`QualificationGates.Default`, scaled by F: the Lab's minimums then exceed the protocol's own 300 trades and 0.05 R, which stay a separate rule; whether the Lab bar suits pairs is an owner question) through `VerdictEvaluator` with N; no pair contributes ≥ 25 % of the net profit; **net** costs. |
| DROP | not KEEP, minima met, upper bound of mean R < 0.05 R (unwidened). **A gross DROP is valid**: costs only lower an absolute R (there is no comparator here). |
| INCONCLUSIVE | otherwise, including a gross result that would otherwise be KEEP. |

Records go to `research.extension_evaluations` (protocol `S`, fold unit `quarter`); the variant grid is reported with N; the selected-pair count per formation, the half-life distribution, the share of formations with no selected pair and the turnover per trade are printed.

## Acceptance criteria
1. **Ledger first** (phase criterion 5). With 2,000 candidates, the ledger gets 2,000 candidate trials (plus the variant trials) **before** any formation statistic or trade is computed (a call-order fake); the verdict uses `Factor(2006)` and prints it; re-running the same quarter appends nothing.
2. **Formation hand case.** A synthetic sector of 4 shares where one pair is constructed cointegrated (P14-12 criterion 3 fixture) and the rest are independent walks: the cointegrated pair is selected, the others not, with the BH table printed; an unavailable test is counted in `m`.
3. **Trading hand case.** A synthetic day with a planted spread excursion to `z` 2.2 then back to 0.4: one long-or-short spread trade, entered at the next 1-minute opens of both legs, exited at `|z| ≤ 0.5`, R matching the hand example's method (paise exact on the fixture); a stop at 3.5; a forced exit at 15:15.
4. **No look-ahead (property).** Changing any bar after a decision changes neither that decision nor the formation of the quarter; changing a daily close inside the formation window can change the selection (control).
5. **Honest states** (phase criterion 6). Fewer than 6 quarters or 300 trades → NOT_EVALUABLE naming each; evidence JSON without a digit for a non-value; `ban_list: unavailable` in the limits.
6. **Concentration.** A fixture where one pair makes 30 % of the net profit is not KEEP (the reason named).
7. **Engine untouched.** `sim-9` goldens unchanged; `PairWalker` reports `pair-1`; a pair run record names both versions.
8. **Migration.** `research.pair_formations(formation_id char(64) PK, family, quarter, window_from, window_to, candidates int, tested int, unavailable int, selected int, detail jsonb, created_at)` and `research.pair_trades(run_id char(64), pair_key, ordinal, entry_ts, exit_ts, side, z_entry, z_exit, exit_reason, qty_y, qty_x, gross_inr numeric, net_inr numeric null, r double precision, labels text[], PK (run_id, pair_key, ordinal))`, insert-only for `xd_app`, nothing for `xd_exec` or the read roles; `Down` drops both.
9. **No decision use.** Architecture test as P14-09 criterion 9; no-order tripwire green.
10. **Determinism.** Same bars and options → same formations, trades, record ids.

## Contract
- **Verbs:** `xd pairs run --from --to [--variant e,L]` (formations and trades; exit 0); `xd pairs eval --family pairs:intraday-1 [--holdout]`.
- **Data:** `AddPairResearch` (criterion 8); records in `research.extension_evaluations`.
- **Settings:** `Pairs:PairNotional` 100000 (10,000-10,000,000), `Pairs:MaxCandidates` 2000 (100-25,000).
- **API:** the shared route lists `pairs:intraday-1`.

## Non-functional
One quarter's formation over 2,000 candidates ≤ 2 s; trading a quarter for 50 selected pairs ≤ 1 minute (UNVERIFIED); the bars are read once per instrument and quarter.

## Test plan
- **Unit:** sizing and R hand example, the session rules, the cap and tie rule, the verdict rows with unavailable inputs.
- **Simulation:** criterion 3; fills and slippage per leg against the single-instrument walker on the same bar (equal fill prices).
- **Property:** criteria 4 and 10; placebo (independent walks) not KEEP at N = 2,006 over 200 seeds.
- **Integration (Timescale):** both verbs on a seeded bar set; grants; ledger order.
- **Architecture:** criterion 9.

## Decisions (researched)
1. **Every candidate is a trial; re-forming is not.** The trial text names the pair and the test, not the window, so N grows with distinct pairs looked at, not with time.
2. **Quarterly re-formation aligned to calendar quarters.** One trading window is one fold; no formation reads its own quarter.
3. **R from the stop distance in spread terms.** The pair has no single price stop; the spread's stop at `|z|` 3.5 gives the risk a cash R would have.
4. **Gross DROP allowed, gross KEEP not.** Without a comparator, costs can only lower R.

## Risks and honest limits
- Pairs evidence for NSE is thin and old (D10); N is large by construction, so only a large effect can be KEEP.
- Today's sector labels and today's F&O list are used for the whole history (survivorship, ISS-22); no ban list exists.
- 2.1 years of bars give about 3 quarters after the first formation: NOT_EVALUABLE until CF-04 Part B.
- Four fills of intraday cash per trade make costs large against a small spread reversion.

## Open questions (owner)
0. The Lab's own gates, scaled by F, are used for the KEEP rule, and the hold-out pass rule (>= 100 net trades, mean above 0, day-block lower bound above 0) is the implementer's conservative choice: keep or change? (ADR 0196.)
1. `PairNotional` ₹1,00,000? **Default: as drafted.**
2. An F&O ban-list source (files dropped by the owner)? **Default: none; `unavailable` is printed as a limit.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer (Opus): no blockers/majors (told to look for any statistic before its ledger row, any formation that reads its trading quarter, any change to `sim-9`, any unavailable read as a pass)
- [ ] Migration `AddPairResearch` with `Down`
- [ ] ADR written; doc 20 §C updated
