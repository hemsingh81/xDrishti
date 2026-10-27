# CF-05 — Cost stress gate (1x / 1.5x / 2x), conservative slippage tiers and the fill-one-minute-later test

- **Status:** Draft (written 2026-10-27; the decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** CF — Confidence programme (a row is added by CF-00; UNVERIFIED, CF-00 not yet written). Touches P5 (Lab verdict and report) and P3 (fill rules, charge sheet).
- **Design refs:** docs/design/11-strategy-lab.md §3-§5 · 16-configuration.md (`costs`, `strategy_lab`) · docs/research/confidence-edge-realism.md §1 and §4 · ADRs 0038 (fill rules), 0042/0044 (charge sheet, bill check), 0059/0063 (gates, report)
- **Needs:** P3-03b/c/f (built), P5-03/05/06 (built), CF-00 (settings home and report-only rule). Related: CF-06 (ledger and hold-out), CF-11 (scorecard rows 4, 5, 14).
- **Size:** about 5.5 agent-days (stress gate 1.5, tiers and delay fill 2, turnover filter and capacity 1.5, bill check 0.5), plus the owner's contract notes. Uncalibrated.
- **Safety:** no. Pure research-side numbers; no order, no broker, no state change. The new gates start **report-only** (below).

## Assumptions (taken as recommended; none is an owner-only decision unless listed under Open questions)
1. **Stress is a re-statement of stored trades, not a new walk-forward.** `SimTrade` carries `SlippageR` (`RFilled - RExact`, never positive) and `Costing` (the 16 charge lines and net R) (read in `Domain/Research/SimTrade.cs`). With `chargesR = RFilled - NetR`, the stressed net R of a trade at multiplier `m` is `RExact + m * (SlippageR - chargesR)`, exact for charges and first-order for slippage (a harsher slippage could also stop a fill from happening; the delay test, not this line, covers that). The stress is applied to the out-of-sample trades the verdict was judged on (the winners' test windows of P5-04); selection is not repeated and the report says so.
2. **Only new gates are added; the existing verdict is untouched** (CF-00 rule: a new gate is report-only beside PASS/MARGINAL/FAIL until the owner turns its `enforce` setting ON). The existing `cost_sensitivity` warning (net expectancy under 0.15 R) stays.
3. **Charges come from the stored sheet and nothing in `src/` holds a rate** (ChargeRates doc comment). This slice adds no rate; it adds an owner task to fill and confirm them.
4. **The thresholds below are proposals** stored as versioned settings (home decided by CF-00; until then the insert-only snapshot pattern of P7-12, `workflow.autonomy_settings`, is the model). Every verdict stores the snapshot hash it used.

## Decisions (researched)
**D1. Stress multipliers and thresholds.** Multipliers `[1, 1.5, 2]` scale **both** charges and slippage. Proposed gates on out-of-sample net expectancy: at 1x the existing gate; at 1.5x at least +0.08 R; at 2x greater than 0. Not multiplied by the trial factor of `MultipleTesting` (they are floors on realism, not significance tests); the 1x gate keeps its factor. The "tradable" label uses the worst of the three. *Why:* a retail edge that dies when costs rise by half is a cost-model artefact; SEBI's own study has loss-makers spending 57 % of their losses on costs and profit-makers 19 % of profits (research §0). *Rejected:* a single 1.5x gate (hides whether the 2x case is at least positive); scaling only slippage (charges are the best-known number but also the one most likely to be mis-entered). *Source:* confidence-edge-realism.md §1.4, §4 row 4; [Business Standard on the SEBI FY23 study](https://www.business-standard.com/amp/markets/news/over-70-intra-day-traders-incur-losses-during-fy23-reveals-sebi-study-124072401110_1.html).
**D2. Conservative slippage tiers (a second preset, not a replacement of the default).** Today `Slip.Amount = (Ticks [+ extra for stop exits]) * tick + BarRangeFrac * range` and an entry never passes the limit; targets get no slippage (read in `Simulation/Walking/Slip.cs`). Add a `TieredSlippage` option: `amount = max(Ticks * tick, tierBps * price) + extraTicks * tick (stop exits) + 0.10 * range`. Tier bps: A 3, B 8; tier C is **excluded** from edge claims (not priced). Tier = the instrument's 20-day median traded value on the entry date (close x volume from stored daily bars), cut points proposed A at least Rs 100 crore, B Rs 20 to 100 crore, C below Rs 20 crore (UNVERIFIED planning numbers; the first task prints the tier table for the owner's universe so the owner can move the cuts). *Why:* a flat "1 tick + 5 % of range" is light for tier B/C and heavy for tier A; no published NSE intraday slippage by tier exists (research §1.2), so the numbers are planning assumptions to be replaced by measurement from paper fills (P11-02 later). *Rejected:* one flat bps for all names (mis-prices both ends); an impact-cost model (needs depth data; P13).
**D3. Fill one minute later (delay stress).** A new `EntryDelayMinutes` fill option (0 default, 1 for the test): the entry fill minute becomes the second minute after the trigger bar; the P3-03b rule is otherwise unchanged (open if within the limit, else the limit if the minute trades back to it, else no trade). The test is run on the **same signals** and compared **per signal**, a signal that no longer fills counting as 0 R. Gate (proposal): expectancy per signal at delay is at least 70 % of the base. *Why:* the missed trades are the ones that ran (adverse selection); a per-fill mean would hide them. *Rejected:* comparing means of filled trades only.
**D4. Capacity and the turnover filter.** `min_turnover` is accepted but not evaluated (read: `FilterNames.MinTurnover` exists; its evaluation is UNVERIFIED, the research note says it is a no-op). Evaluate it from stored bars (20-day median traded value) and add a capacity check: order value at most 1 % of the fill minute's traded value and at most 0.5 % of the 20-day median daily value (UNVERIFIED rule of thumb). A breach marks the trade `capacity_breach`; the evidence for the "tradable" label excludes tier C and breaches and prints how many.
**D5. Bill check before any number is believed.** The sheet has a field for every line (`ChargeRates`: brokerage pct/cap/min, STT buy/sell, exchange, SEBI per crore, stamp, other, DP per delivery sell; GST in `ChargeSchedule`) and a "checked against bills" flag, set only by `cost-sheet check` on the owner's Dhan transaction reports (P3-03f). The owner supplies five contract notes (three intraday, two delivery incl. a sell day with DP); numbers to confirm are listed below. Until `checked` is true every stress result and tier label carries "costs not checked against a bill: stress gates cannot PASS" (a gate reads `unavailable`, which counts as fail, CLAUDE.md).

Charge lines to confirm from a contract note (UNVERIFIED for Dhan; the shape is Zerodha's public page, [zerodha.com/charges](https://www.zerodha.com/charges)):

| Line | Intraday equity | Delivery equity | Status |
|---|---|---|---|
| Brokerage | Rs 20 or 0.03 %, lower, per executed order (doc 16) | Dhan delivery brokerage: UNVERIFIED (doc 16 says 0) | confirm |
| STT | 0.025 % sell | 0.1 % buy and sell | cash rates unchanged by the April 2026 F&O change (research §1.1) |
| Exchange transaction (NSE) | 0.00297 % or 0.00322 % of turnover (snapshots disagree); use 0.0035 % as the conservative figure until a note shows the rate | same | confirm |
| SEBI fee | Rs 10 per crore | same | confirm |
| Stamp duty | 0.003 % buy | 0.015 % buy | delivery rate UNVERIFIED |
| GST | 18 % of (brokerage + exchange + SEBI) | same | base marked UNVERIFIED in P3-03c |
| DP charge | none | about Rs 13.5 plus 18 % GST (Rs 15.93) per scrip per sell day at big brokers; Dhan figure UNVERIFIED | confirm |

## Goal
A Lab result says, in numbers, whether the edge survives costs and slippage that are 1.5 and 2 times the model, and an entry that lands a minute late, using conservative per-liquidity-tier slippage, and says plainly when the cost sheet has not been checked against a bill.

## Scope
**In:** pure Domain `CostStress` (multipliers, stressed net R, the three results, worst-of), `DelayStress`, `TieredSlippage` option inside `FillRules`/`SlippageConfig` (new fields; canonical JSON and run key change only when the option is used, so existing runs and their keys are unchanged), `LiquidityTier` and `Capacity` (pure), their evaluation in `LabFullTest`/`LabRobustness` output (`confidence.costStress` member of the stored result document; jsonb, no migration for the document), report section and warnings in `LabReportBuilder`, settings, the owner bill-check task, ADR, docs 11 and 16, `gen-api`.
**Out:** the enforcement of the gates in the verdict (turning `enforce` ON is an owner act and a later change of one setting); impact cost and depth (P13); measured live slippage (P11-02); the scorecard (CF-11); delivery-holding (swing) cost realism beyond DP, which swing P3-03d already models (UNVERIFIED how well).

## Acceptance criteria
1. **Charge hand example (intraday, Rs 1,00,000 per leg, Rs 2,00,000 turnover, 1 % stop so 1R = Rs 1,000).** brokerage 20 + 20 = 40 (0.03 % of Rs 1,00,000 is Rs 30, the Rs 20 cap applies); STT 0.025 % x 1,00,000 = 25; exchange 0.00297 % x 2,00,000 = 5.94; SEBI Rs 10 per crore x 2,00,000 = 0.20; stamp 0.003 % x 1,00,000 = 3.00; GST 18 % x (40 + 5.94 + 0.20) = 8.3052. Total 82.4452, i.e. **0.0824 R**. Same trade as delivery, brokerage 0: STT 100 + 100 = 200; stamp 0.015 % x 1,00,000 = 15; exchange 5.94; SEBI 0.20; GST 18 % x (5.94 + 0.20) = 1.1052; DP 15.93: total 238.1752 = **0.2382 R**. With a 0.3 % stop (1R = Rs 300) the intraday Rs 82.45 is **0.2748 R**. The existing `CostModel` reproduces the first two to the paisa under the numbers above (test against a TEST sheet; never against real rates).
2. **Stress arithmetic.** One trade `RExact = +1.00`, `SlippageR = -0.05`, net R 0.87 gives `chargesR = 0.95 - 0.87 = 0.08` and net R at m = 1 / 1.5 / 2 of 0.87 / 0.805 / 0.74 (`1.00 - 0.13m`). The mean over a set equals the mean of the per-trade values (property test over random sets).
3. **Gate on the research examples.** Net expectancy at 1x +0.20 R with cost-plus-slippage 0.12 R gives 1.5x = 0.20 - 0.06 = **+0.14** (at least +0.08: pass) and 2x = **+0.08** (greater than 0: pass); tradable label = pass. Net at 1x +0.10 R with 0.12 R cost gives 1.5x = **+0.04** (fail) and 2x = **-0.02** (fail); label = not tradable. A NaN or `unavailable` input fails (unit).
4. **Tiered slippage by hand.** Entry price Rs 1,000, tick Rs 0.05, minute range Rs 4.00, 1R = Rs 10 per share. Default rule: entry slip `0.05 + 0.05 x 4 = 0.25` = **0.025 R**; stop exit `(1 + 2) x 0.05 + 0.20 = 0.35` = **0.035 R**. Tier A conservative: entry `max(0.05, 3 bps x 1,000 = 0.30) + 0.10 x 4 = 0.70` = **0.07 R**; stop exit `0.30 + 2 x 0.05 + 0.40 = 0.80` = **0.08 R**; a target exit stays 0. A stop-out round trip: 0.15 R against 0.06 R by default. The entry never passes the limit (limit = trigger close + 0.1 %); fills lie on the tick grid; slippage never improves a price (property test, as P3-03b).
5. **Delay test by hand (per signal).** 100 signals, base: all fill, mean 0.20 R, total 20 R. Delayed: 90 fill with total 16 R, 10 no longer fill. Per signal 0.16 = **80 %** of base: pass. Per fill it would read 0.178 (89 %), which the report must not use. A delayed total of 13 R gives 65 %: fail. A base of zero or negative reads `not evaluable`, not pass.
6. **Tiers and capacity.** A name with a 20-day median traded value of Rs 450 crore is tier A; Rs 60 crore tier B; Rs 12 crore tier C (excluded, counted and printed). Order Rs 1,00,000 in a minute that traded Rs 40 lakh is 2.5 % of the minute: `capacity_breach` (1 % cap = Rs 40,000); in a minute that traded Rs 2 crore it is 0.5 %: ok. 20-day median Rs 120 crore: 0.5 % cap is Rs 60 lakh.
7. **Unchanged runs.** A run with none of the new options has the same run key, trades and verdict as before (golden test on a stored P5 fixture); the stress block is absent, not zero.
8. **Report-only.** The new gates appear in the report as rows with value, threshold, status; `enforce` OFF: the verdict outcome is identical with and without them (test). With `enforce` ON a failed 2x gate caps the outcome at MARGINAL, never raises it.
9. **Checked sheet.** With the sheet not `checked`, the stress rows read `unavailable: costs not checked against a bill` and count as fail wherever a scorecard reads them (CF-11). After `cost-sheet check` writes `checked`, the label disappears (integration with a fake store).
10. **Determinism and logging.** Same input, same bytes; no strategy text, trade or secret in any log line; Architecture tests green.

## Contract
- **API:** no new endpoint. `GET /api/lab/runs/{id}` and `.../report` gain an optional `confidence.costStress` member (multipliers, per-multiplier expectancy and trades, worst-of, delay result, tier counts, capacity breaches, `costsChecked`). `POST /api/lab/runs` accepts optional `stress` (`true` default for `robust`, off for `quick`/`full` to keep their cost) and `slippage: "default"|"conservative"`. OpenAPI regenerated.
- **Data:** no migration for the result document (jsonb). Settings `confidence.cost_stress.*` (multipliers, 1.5x floor 0.08, 2x floor 0, delay retention 0.70, tier cuts, tier bps, range fraction 0.10, participation 1 % and 0.5 %, `enforce` false) in the versioned settings home.
- **UI:** the Lab report panel (`frontend/src/features/lab/components/ReportPanel.tsx`) shows a "Costs and delay" block (table of three multipliers, delay line, tier counts, the sheet-checked label). Status not by colour alone.
- **Jobs:** inside the existing `robust` stage; progress counts the extra passes.

## Non-functional
The stress is arithmetic over stored trades (linear in trades, kilobytes). The delay pass is one extra walk of the winning cells' signals (not the candidate-exit grid); target under 2x the time of the robustness stage on the owner's data (not timed; UNVERIFIED). Deterministic, no random. Settings changes are append-only and audited.

## Test plan
Unit (Domain): charge examples through `CostModel`, `CostStress`, `DelayStress`, `TieredSlippage` hand tables, `LiquidityTier`, `Capacity`, property tests (never improves a fill; stress mean equals mean of stresses; monotone in m). Unit (Application): report block and warnings; `enforce` OFF/ON. Integration: a robust run on the seeded fixture returns the block; an old stored result without it still reads. Frontend (MSW): the block renders, the unavailable label shows. Architecture: Domain stays free of I/O.

## Risks
The 2x charge scaling over-penalises a fixed Rs 20 brokerage (scaling the total is a deliberate simplification, stated). Tier cuts and bps are guesses until paper fills exist. Few out-of-sample trades make every stress mean noisy; this slice does not add power (CF-01).

## UNVERIFIED
Whether `Costing` exposes a ready `chargesR` or only the 16 lines and net R (derived above either way). Dhan brokerage, DP and stamp on delivery, the NSE transaction rate, GST base. Whether stored daily bars carry volume for a 20-day median value. Whether `min_turnover` is evaluated anywhere. The speed of the delay pass on real data. All numbers marked proposals.

## Open owner questions
1. Five contract notes (three intraday, two delivery including a DP day) to fill and check the sheet. 2. The tier cuts (Rs 100 crore / Rs 20 crore) and bps (3, 8). 3. Is dropping tier C from edge claims acceptable, or should tier C be priced at 15 to 30 bps and kept? 4. When the stress gates go from report-only to blocking (owner act, fresh password).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); no migration for the result document
- [ ] Design docs/ADR updated (docs 11, 16); `scripts/build-docs.sh`

## Review fixes 2026-10-27
1. AC6 addition: evidence that comes only from tier C is a hard fail in CF-11 (D3 of CF-11), and tier C trades are excluded from the "tradable" label; the report prints how many were excluded.
