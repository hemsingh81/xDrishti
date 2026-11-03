# P14-09 — Expression evaluation and verdict: protocol O for `option_buy` on P13-05's core, ledger rows before any number, paired `R_equiv` with day-block bounds, the hold-out read once, and NOT_EVALUABLE with the missing quantity named

- **Status:** Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-09 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Expression evaluation and verdict"); the phase's kill criterion for the option family; phase acceptance criterion 6.
- **Design refs:** P14-00 protocol O (items 1 to 6), D7, D8, D9, risks 1, 4 · P13-05 (the shared core: `CalendarFolds`, `BlockBootstrap`, ledger-first trials, `research.extension_evaluations`, hold-out once) · P15-10 (the record and route pattern for non-model protocols) · P8-00 D3 (evidence block, honest states) · ADR 0059 (ledger, `VerdictEvaluator`), 0080 (verdict words), 0183 (comparison core), 0186 (protocols F, X, P, L stored in the same table). New ADR: **next free number at build time**.
- **Needs:** P14-08 (expression runs), P13-05 (built), P5-03/P5-04 (built: ledger, gates, the `full` Lab test whose OOS windows give the cash population), P8-05 (built: evidence block shape). **History and owner acts (defaults if missing):** 5 years of underlying bars (CF-04 Part B, ISS-19; D9) — without it the fold count is short and the verdict is NOT_EVALUABLE; the P14-03 backfill (probe, acceptance, switch); a spread (60 usable sessions or the owner's assumption, P14-07) — else NOT_EVALUABLE `spread_unavailable`; the owner's option charge lines — else gross and at best INCONCLUSIVE. **Needed by:** P14-10 (runs after this slice has stored any verdict for `expr:option_buy`), P14-16, P14-17.
- **Size:** 3 agent-days (estimate).
- **Safety:** no — read-only over stored runs; writes only ledger rows and its own records. A KEEP enables research and paper discussion only: `expression` stays `cash` on every decision path (P14-06) and no slice of this plan builds a live option path.

## Goal
One verb gives the option family a stored verdict — KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE — computed by protocol O exactly as pre-registered, counted in the test ledger before any number is read, with its evidence block. Today's data gives NOT_EVALUABLE with each missing quantity named; nothing prints a digit for a non-value.

## Verified facts (read 2026-11-03 in `backend/src`)
- `Domain/Learning/Extensions` (P13-05, P15-10) holds `CalendarFolds.Cut(rows, sessions, options)` (month or calendar-quarter windows, embargo in sessions, purge), `BlockBootstrap.Bounds(blocks, draws, seed, level)` (paired blocks, SplitMix64 seed 1337, positions `ceil(0.10·B)` and `ceil(0.90·B)`, fewer than 2 blocks → unavailable), `ClusterBootstrap.Difference`, `VerdictTable.Decide(missing, failed, unrepairable)`, `ProtocolIdentity.ToRecord` (deterministic `evaluation_id`), `EvidenceBlock`, and `ExtensionFamilies.TrialOf(family, text, from, to)` (trial hash = SHA-256 of family + text; run id over the data window; prefix `p15-ledger/1`).
- `research.extension_evaluations`: `family varchar(32)`, `protocol varchar(8)`, `fold_unit in (month, quarter, none)`, verdict word check, unique partial index `(family, version_key) WHERE holdout_read` (hold-out once); insert-only for `xd_app`, nothing for `xd_exec` or the read roles.
- `ExtensionFamilies.All` lists the five P15 families and **is iterated by `EventsPhaseCheck` and `GetEventsPhaseCheckHandler`**; `ExtensionEvaluationReaders` refuses any other family (`extensions.invalid_query`). Adding P14 families to `All` would put them into the P15 phase check.
- `VerdictEvaluator.Evaluate(QualificationGates, OosEvidence, variantCount, minFoldsForPass)`; `OosEvidence(OosTrades, ExpectancyR, ProfitFactor, Folds, PositiveFolds, MaxDrawdownR, CalibrationErrorPp, Method)`; `MultipleTesting.Factor(N) = 1 + 0.25·log2 N`; `HoldOut.Reserve(rows, months = 6)`.

## Scope
**In:**
- Domain (pure): `ProtocolO` (pairing statistics, folds via `CalendarFolds` quarter unit, `BlockBootstrap` at day blocks and 5-session blocks, the verdict rows below via `VerdictTable`), `ExpressionFamilies` (`expr:option_buy`; P14-10, P14-13, P14-15 add theirs) with its own `IsKnown`, `ExpressionTrial` (canonical trial text from the profile hash and the cash population definition; the ledger run id with prefix `p14-ledger/1`).
- Application: `EvaluateExpressionHandler`, verb `xd options expression-eval --family expr:option_buy --run <full-run-key> [--holdout]`; the ledger append before any computation.
- The route `GET /api/research/extensions?family=` accepts `ExtensionFamilies ∪ ExpressionFamilies` (the P15 phase check keeps iterating `ExtensionFamilies.All` only; a test pins that).
- Tests, ADR, doc 20 §C (verdicts).

**Out:** spreads and condors (P14-10); pairs, basis; any switch or scoring use; a screen (P14-16); a futures comparison (no futures bars: the `future` baseline is NOT_EVALUABLE `no_futures_bars` and printed as such).

## Protocol O as built
- **Population.** The trades in the OOS test windows of one stored `full` Lab run (P5-04) of a strategy, on underlyings with option series, re-marked by P14-08 under a profile. Unpaired trades are counted by reason (never dropped); an unpaired share above 30 % is printed as a limit.
- **Unit and difference.** Per paired trade `d = R_equiv(option) − R_equiv(cash)` on the same cost basis (P14-08).
- **Folds.** Calendar quarters of the trade date via `CalendarFolds` (quarter unit, embargo 5 sessions, purge = hold + 1 bar). No model is fitted, so every quarter with at least `MinPairedPerFold` (20) paired trades is evaluable. Hold-out = the last 6 months (`HoldOut.Reserve`), not read without `--holdout`.
- **Bounds.** Paired block bootstrap of `d` (`BlockBootstrap`): blocks of 1 session and, as the stated sensitivity, 5 sessions; 10,000 draws, seed 1337, one-sided 90 %. KEEP needs both lower bounds; DROP uses the larger upper bound.
- **Trials and N.** Each profile (strike rule, expiry rule, exit rule, premium stop, liquidity filter) evaluated on a population is one ledger trial in family `expr:option_buy`, appended **before** its numbers are computed (the core refuses a trial hash missing from the family's list, as P13-05 does). N = distinct trial hashes of the family; `F = Factor(N)`. The primary comparison is the pre-registered profile `atm`, `nearest_safe`, cash-mapped exits, no premium stop, default liquidity; every other profile is secondary and counted.

| Verdict | When (all thresholds are options printed in the record) |
|---|---|
| NOT_EVALUABLE | evaluable folds < 6; paired OOS trades < 400; no spread (`spread_unavailable`); a bound `unavailable`; no paired trade at all (`no_paired_trades`, for example no stored run trades an index). Each missing quantity is named (`folds 2 of 6`, `paired trades 118 of 400`). |
| KEEP | all: (a) the expression's own OOS evidence (`R_equiv` per fold as `OosEvidence`, method `walk_forward`) passes `VerdictEvaluator` with N; (b) lower bound of mean `d` ≥ `0.05 R × F` at both block lengths; (c) mean `d` > 0 in ≥ `ceil(2/3 × folds)` evaluable folds and in the **latest rule epoch** (from 2025-09-01, epoch `nifty_expiry_tuesday`) with its n printed (n < 30 there is `unavailable`, so not met); (d) **net**: the owner's option and cash charge lines both present (a gross result fails (d)); (e) Black-76 fallback share ≤ 5 % of paired OOS trades. |
| DROP | not KEEP, ≥ 6 evaluable folds, ≥ 400 paired trades, **net** (as (d)), and the upper bound of mean `d` < 0.05 R (not widened). |
| INCONCLUSIVE | otherwise — including every **gross** result, whichever way it points: the half-spread is in both gross and net option figures, but the charge lines are not, and cash charges in R can exceed option charges in R (cash turnover per rupee of risk is far larger), so a gross `d` can be biased either way. A gross result whose upper bound is below the margin says so in its limits (`gross upper bound below margin`). |

An `unavailable` input never passes a rule and never supports DROP (P13-05). The record also prints: the unpaired shares by reason, the sizable-only comparison (lot feasibility, P14-07) as a secondary line, the `spread_from_recent_archive` and `expiry: derived` shares, the measured standard deviation of `d`, the design effect (bootstrap SE ÷ naive SE) and the minimum detectable effect at the achieved n (O.5 power note: with SD 1.5 R, n = 400 gives SE 0.075 R and an MDE of about 0.19 R).

**Hold-out.** Once, after a stored development KEEP: mean `d` on the hold-out with ≥ 100 paired trades; **confirmed** iff it is positive and its lower bound at day blocks is above 0; NOT_EVALUABLE under 100; the hold-out record has `holdout_read = true` and the partial unique index refuses a second (`extensions.holdout_used`).

## Acceptance criteria
1. **Hand values.** Five paired trades with `R_equiv` option [1.2, −1.0, 0.4, 2.0, −0.6] and cash [1.0, −1.0, 0.5, 1.5, −0.5] give `d` = [0.2, 0, −0.1, 0.5, −0.1], mean 0.10. Margins: N = 1 → 0.05 R; N = 4 → 0.075 R; N = 16 → 0.10 R. Six evaluable folds need four positive.
2. **Placebo and planted effect (property, 200 seeds).** `d` drawn with mean 0: not KEEP at N = 1 and N = 16. A planted mean `d` of 0.4 R with 7 quarters and 600 pairs: KEEP; the same with 5 evaluable quarters: NOT_EVALUABLE `folds 5 of 6`; with 350 pairs: NOT_EVALUABLE `paired trades 350 of 400`.
3. **Ledger first.** A ledger fake that records call order shows the trial appended before any statistic; the core refuses a missing hash (`extensions.trial_not_in_ledger`) with zero computations; a re-run appends nothing and stores nothing new (same `evaluation_id`).
4. **Honest states.** Each NOT_EVALUABLE reason and the gross-INCONCLUSIVE case are reached by fixtures; the evidence JSON has the text `unavailable` and no digit where a value is unavailable (render test).
5. **Fallback cap.** A planted KEEP with 6 % `bs_fallback` trades is INCONCLUSIVE with the reason; at 5 % it can be KEEP.
6. **Latest epoch.** A planted effect positive overall but negative after 2025-09-01 (n ≥ 30) is not KEEP; with n = 12 after it, KEEP is not met (`latest epoch n 12 of 30`).
7. **Hold-out once.** Without `--holdout` no hold-out trade is read (a fake reader records the dates); with it, one record; a second is refused `extensions.holdout_used`.
8. **Families kept apart.** `GET /api/research/extensions?family=expr:option_buy` lists the records; `xd events phase-check` output is byte-identical before and after this slice (golden).
9. **No decision use.** Architecture test: the protocol, the families and the records are referenced only by the verb, the route reader, P14-16 and tests; nothing in selection, planning, paper, live or `XDrishti.Execution` reads them; the no-order tripwire is green.
10. **Determinism.** Same stored inputs in any order → the same record and id.

## Contract
- **Verb:** `xd options expression-eval --family expr:option_buy --run <full-run-key> [--profile <hash>] [--holdout]` (exit 0 whatever the verdict; 1 unknown run, profile or family; 2 bad line).
- **API:** the existing `GET /api/research/extensions?family=` and `/{id}` accept the expression families (`scripts/gen-api.sh` if the summary text is part of the contract).
- **Data:** none new (`research.extension_evaluations`, protocol `O`, fold unit `quarter`; `lab.test_ledger`).

## Non-functional
One evaluation over 10,000 paired trades with two block lengths ≤ 30 s (UNVERIFIED); logs `expression.evaluated` with family, verdict and counts only.

## Test plan
- **Unit:** criterion 1, the verdict rows one by one with unavailable inputs, the epoch split, the fallback cap.
- **Property:** criterion 2, determinism, row-order invariance.
- **Integration (Timescale):** the verb over a seeded full run and expression run; hold-out index; route listing; phase-check golden.
- **Architecture:** criterion 9.

## Decisions (researched)
1. **Records in P13-05's table, its route, its bootstrap and its ledger rule.** One verdict mechanism for P13, P14 and P15; P15-10 already dropped a phase-specific route for the same reason (`/api/research/pead`). The plan's `/api/research/expressions` is replaced by the shared route.
2. **A separate family list.** `ExtensionFamilies.All` is the P15 phase check's list; P14's families must not appear there (P14-17 has its own check).
3. **OOS cash trades only.** Re-marking in-sample cash trades would let the cash strategy's own selection flatter both sides; the `full` test's OOS windows are the population.
4. **No verdict either way on gross.** The exit criterion says "after costs", and the sign of the cost difference between an option and a cash trade of the same rupee risk is not known without the lines (cash pays STT and charges on about 100 × the risk in turnover at a 1 % stop; an option on a few times the risk), so a gross result is INCONCLUSIVE, never KEEP or DROP.

## Risks and honest limits
- Expected first answer: NOT_EVALUABLE (`folds 0 of 6` until CF-04 Part B; `spread_unavailable` until 60 usable sessions or an owner assumption). The latest epoch holds about one year (P14-00 risk 1), so even with data it is mostly INCONCLUSIVE.
- Only a large edge (about 0.2 R per trade) is detectable at 400 pairs (power note).
- Survivorship (ISS-22) and the recent-spread assumption flatter both sides unequally; both are printed.

## Open questions (owner)
1. Read the hold-out now or wait? **Default: wait; `--holdout` is an explicit act after a development KEEP.**
2. Load the option and cash charge lines (ISS-25, P14-07)? **Default if not: every result is INCONCLUSIVE at best, so the family stays disabled (`expression` stays `cash`) — the kill criterion's default.**

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer (Opus): no blockers/majors (told to look for any number before its ledger row, any unavailable read as a pass, any hold-out read without the flag, any P14 family in the P15 phase check, any decision-path reader)
- [x] No migration; contract regenerated (the route summary names the family; built 2026-11-04)
- [x] ADR written ([ADR 0198](0198-protocol-o-judges-the-option-expression-against-cash-on-paired-out-of-sample-trades-after-counting-each-profile-in-the-ledger-first-with-the-hold-out-cut-before-any-number-and-no-gross-result-ever-a-keep-or-a-drop.md)); doc 20 §C updated
