# P14-10 — Defined-risk structures: debit spreads as an expression of the cash signals, credit spreads and iron condors on index options only in a stored range regime, each with the maximum loss known at entry and its own family and verdict

- **Status:** Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-10 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Defined-risk structures"); runs after P14-09 has stored any verdict for `expr:option_buy` (the plan's gate; the results are separate families).
- **Design refs:** docs/design/20 §C · P14-00 D5 (long options and defined-risk structures only; no naked short; no margin computed), D6, protocol O items 4 to 6 ("debit spreads and condors are separate families with their own ledger and verdicts, each pre-registered once") · P14-01 "If the owner skips it" (credit structures NOT_EVALUABLE without measured spreads) · ADR 0038 (worse outcome), 0183/0186 (records). New ADR: **next free number at build time**.
- **Needs:** P14-08 (re-mark machinery, expiry calendar, held-strike series), P14-09 (protocol O and its families), P14-07 (costs, spread model), P14-05. **Regime (P6-02, needs P3-01d; both not built):** without stored regime labels, credit spreads and condors are NOT_EVALUABLE `regime_unavailable` and only debit spreads are evaluated. **Measured spreads:** credit spreads and condors need a **measured** spread model (≥ 60 usable chain sessions, P14-07); an owner assumption is not enough for them (4 or 2 half-spreads decide their result).
- **Size:** 3.5 agent-days (estimate).
- **Safety:** no — research re-marking and simulation of option structures from stored bars. A short leg exists only inside a structure whose loss is capped at entry; nothing computes margin, places, plans or sizes an order.

## Goal
The Lab can ask, with the same honesty as P14-09, whether a cash signal is better expressed as a debit spread, and — only where the regime is known — whether an index credit spread or iron condor has an out-of-sample edge after costs. Each structure is its own family with its own ledger count and verdict.

## Verified facts (read 2026-11-03)
- The rolling archive holds ±10 offsets for NIFTY and BANKNIFTY and ±3 for the 20 backfilled shares, calls and puts (ADR 0177): a structure whose legs fit inside the window can be priced leg by leg from vendor bars; wider wings cannot.
- No regime tagger exists (roadmap: P6-02 Draft, needs P3-01d); `CellId.Regime` is `*` everywhere.
- `options.chain_snapshots` (our spreads) start when the recorder is switched on; no measured spread exists today.
- `research.expression_runs` / `expression_trades` (P14-08) carry one option per trade; a structure needs one row per leg.

## Scope
**In:**
- Domain (pure): `StructureLeg`, `Structure` (`debit_spread`, `credit_spread`, `iron_condor`), `StructureRemark` (legs from held-strike series, the structure's value per bar, max loss and max gain at entry, exits), `CondorSchedule` (entries of the range family), parameter lists for `ExpressionProfile`.
- Application: `RemarkStructureHandler` (verb `xd options remark --profile <hash>` gains structure profiles) and `EvaluateExpressionHandler` with the families `expr:debit_spread`, `expr:credit_spread`, `expr:iron_condor`.
- Migration `AddExpressionLegs`: `research.expression_legs` (one row per leg of a paired structure).
- Tests, ADR, doc 20 §C.

**Out:** naked short options, ratio spreads, calendars (out of P14); margin; any live use (a multi-leg order needs P12 order groups — not in this plan); building the regime tagger (P6-02); stock condors (thin; the ±3 window cannot hold the wings).

## The rules
- **Debit spread** (expression of a cash trade, paired with protocol O). BUY → bull call spread: long `K1` = the P14-08 strike rule, short `K2 = K1 + w` strike steps; intraday SELL → bear put spread (long `K1`, short `K1 − w`). `w` ∈ {2, 4} steps (indices), {2} (shares, because of ±3). Same entry minute, expiry rule, exit mapping and expiry exit (D6) as P14-08. **Max loss = net debit, max gain = `w × step − debit`**, both known at entry and stored. Units: `units = risk ÷ net debit` (the cash trade's rupee risk buys exactly that maximum loss); `R_equiv` as in P14-08. Both legs must be readable at entry; a leg that leaves the window later uses the P14-08 fallback rule per leg (counted), else `unpaired/strike_left_window`.
- **Credit spread** (expression of a cash trade on an index, paired with protocol O; **only on a session whose previous-session regime is `range`**): BUY → bull put spread (short put at `K1`, long put `K1 − w`); SELL → bear call spread. Max loss `w × step − credit`; units `risk ÷ max loss`.
- **Iron condor** (an index range family, **not** a cash expression; unpaired): on the first session of each expiry week at 09:30 when the previous session's regime is `range`, sell the call and put `k` steps from ATM and buy wings `k + 2` steps, nearest weekly that is at least 2 sessions away; exit at the close of the session before expiry (D6) or when the loss reaches `m × credit`. Pre-registered primary: `k = 6`, `m = 1`; counted variants `k ∈ {4, 6}` × `m ∈ {1, 2}` (N = 4). Max loss `2 × step − credit` per side (one side can lose), stored at entry. R = net rupees ÷ max loss.
- **Prices and the worse outcome.** Every leg priced from vendor bars at the open of the first bar at or after the event minute; the structure's stop or target inside a bar takes the worse leg combination (ADR 0038: for a short leg the bar high, for a long leg the bar low, at the same time). Costs: P14-07 charges on each leg and **one half-spread per leg per side** (4 for a spread round trip, 8 for a condor).
- **Families and verdicts.** `expr:debit_spread` and `expr:credit_spread` use protocol O unchanged (P14-09). `expr:iron_condor` uses **protocol R** (its own OOS evidence only, there is no cash twin): KEEP needs ≥ 6 evaluable quarters, ≥ 200 condors, the Lab gates through `VerdictEvaluator` with N, lower bound of mean R (day blocks and 5-session blocks, 10,000 draws, seed 1337) ≥ `0.05 R × Factor(N)`, mean R > 0 in ≥ 2/3 of folds and in the latest epoch (n ≥ 30), net costs, measured spreads, fallback share ≤ 5 %; DROP: not KEEP with the data minima met and the upper bound < 0.05 R; else INCONCLUSIVE; missing data → NOT_EVALUABLE with the quantity named. Ledger first, hold-out once, records in `research.extension_evaluations` (protocol `R`, fold unit `quarter`), as P14-09.

## Acceptance criteria
1. **Max loss at entry.** Hand case: NIFTY step 50, bull call `K1` = 22,000 at 180, `K2` = 22,100 at 120 → debit 60, max loss 60, max gain 40 per unit; with a cash risk of ₹1,000 → 16.667 units; a cash stop exit with the spread at 30 gives −₹500 gross, `R_equiv` −0.5. Condor hand case: credit 40 on 100-point wings → max loss 60 per unit. A structure whose computed loss in any bar would exceed its stored max loss (plus costs) fails a property test (it never does).
2. **Worse outcome.** A bar where the short leg's high and the long leg's low both hit the structure stop gives the worse combination.
3. **Gating.** With no regime rows, credit spreads and condors store NOT_EVALUABLE `regime_unavailable` without reading a bar (a fake reader records zero reads); with a spread model built from 59 sessions or only an owner assumption, they store NOT_EVALUABLE `spread_not_measured`; debit spreads run with the assumption, labelled.
4. **Families.** Each structure has its own family, ledger rows and N; a trial of one never counts in another; condor variants make N = 4 (`Factor 1.5`).
5. **Unpaired and window.** A stock debit spread with `w = 4` is refused at profile validation (`width_outside_window`); a leg leaving ±N uses the per-leg fallback (counted) or is `strike_left_window`.
6. **Honest states and determinism.** As P14-09 criteria 4 and 10, per family.
7. **Migration.** `research.expression_legs(run_id, trade_key, leg int2, side, position char(5) in (long, short), strike_px, entry_premium_px, exit_premium_px, labels text[], PK (run_id, trade_key, leg))`, insert-only for `xd_app`, nothing for `xd_exec` or the read roles; `Down` drops it.
8. **No decision use.** As P14-09 criterion 9; `XDrishti.Execution` references no structure type; the no-order tripwire is green.

## Contract
- **Verbs:** `xd options remark --run … --profile <structure-profile>`; `xd options condors --profile <hash> --from --to` (simulates the range family); `xd options expression-eval --family expr:debit_spread|expr:credit_spread|expr:iron_condor`.
- **Data:** `AddExpressionLegs`; records in `research.extension_evaluations`.
- **API:** the shared `GET /api/research/extensions?family=` lists the three families.

## Non-functional
A five-year condor simulation on NIFTY ≤ 1 minute (about 260 entries; UNVERIFIED); structure re-marks within the P14-08 budget per leg.

## Test plan
- **Unit:** criterion 1 hand cases, the worse-outcome table, condor schedule across a holiday and the 2025-09-01 weekday change, units.
- **Property:** loss never beyond the stored max loss; no look-ahead; determinism.
- **Integration (Timescale):** a seeded archive through both verbs; grants; gating with and without regime and spread models.
- **Architecture:** criterion 8.

## Decisions (researched)
1. **Credit structures need measured spreads.** Four to eight half-spreads per round trip are most of a condor's edge; an assumed number would decide the verdict (P14-01 recommendation).
2. **The condor is its own family with protocol R.** It has no cash twin to pair with; calling it an "expression" would compare it with nothing.
3. **Units from the maximum loss.** A defined-risk structure sized so that its maximum loss equals the cash trade's risk compares like for like on the worst case.

## Risks and honest limits
- About 52 weekly condor entries a year in a range regime only: 200 condors need most of five years; NOT_EVALUABLE is the likely answer even with data.
- Index weekly history crosses the 2024-11-20 (Bank Nifty weeklies end) and 2025-09-01 (Tuesday expiry) changes; epochs are printed.
- Short legs carry the expiry-day extra margin and pin risk that D6 avoids by exiting a session early; gaps through both legs are capped by the structure, not by a stop.

## Open questions (owner)
1. Build P14-10 before the regime tagger (P6-02) exists? **Default: build debit spreads fully and credit/condor code with their NOT_EVALUABLE gate; they evaluate when regimes are stored.**
2. The condor parameters (`k` 6, `m` 1, wings 2 steps)? **Default: as drafted, pre-registered.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer (Opus): no blockers/majors (told to look for any naked short leg, any loss beyond the stored maximum, any credit verdict on an assumed spread or without a regime, any cross-family trial)
- [ ] Migration `AddExpressionLegs` with `Down`
- [ ] ADR written; doc 20 §C updated
