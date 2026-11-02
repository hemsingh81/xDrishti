# P15-10 — Evaluation and verdicts: protocols F, X, P and L, ledger rows before results, insert-only verdict records with evidence, the event-filter verdict port, and the honest NOT_EVALUABLE

- **Status:** Draft (written 2026-11-02 under the owner's delegation; the owner's Approved is needed before an agent builds it)
- **Phase / roadmap row:** P15 — slice P15-10 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Evaluation and verdicts"); never cut.
- **Design refs:** P15-00 "Evaluation protocols" (F, X, P, L), D8-D11 · P13-00 "Evaluation protocol" (the paired comparison, power) · P8-00 D3 (evidence block, honest states) · docs/design/09 §3, 19 §1 · ADR 0059 (ledger), 0067 (validation harness), 0069 (entry-window pattern), 0080 (phase verdict words), 0182 (fail closed; "P15-10 must report the share of entries lost to `no_upcoming_entry`"). New ADR: **next free number at build time**.
- **Needs:**
  - **P13-05, the shared comparison core** (spec [P13-05](P13-05-feature-set-comparison-core.md) Draft; a slice of its own, built before P15-05). It takes the fold unit as a parameter (preset `ForFilings()`: calendar quarters, season blocks), owns `research.extension_evaluations` and its store, and checks the ledger hash before computing. This slice cannot be Approved until P13-05 is Approved.
  - P15-05 (`ev-1`, `fu-1`, `research.trade_features_ext`), P15-06 (built: `EventCalendarSnapshot`, `IEventFilterVerdict`, `ApplyEventFilterAutoHandler`), P15-09 (`PEAD`), P15-08 (`llm-1`, set D), P5-03/P6-05 (ledger, walk-forward, built).
  - **History:** the CF-04 backfill (Draft; needs the owner's terms read and allow-list act) for X (≥ 6 quarterly folds) and P (20 seasons of bars). **Owner files:** the calendar rows (with intimation times) and results filings over the evaluated window. **Charge sheet** (ISS-25): default gross, labelled.
- **Size:** 3 agent-days (estimate; P13-05's 4 days are counted in P13).
- **Safety:** no for the evaluation (read-only over stored runs, writes only its own records and ledger rows). The one path with effect is the event-filter auto option of P15-06: this slice implements `IEventFilterVerdict`, so a stored `KEEP` can turn the filter ON **only** where the owner has set `approval = auto`; that path is P15-06's (fresh-password owner act to arm it, recorded as `auto`, never as an owner approval, never on `unavailable`). No order, no size, no scoring change.

## Goal
One verb produces, for each P15 family, a stored verdict — KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE — with its evidence block, computed by the pre-registered protocol and counted in the test ledger before any number is read. A thin history yields NOT_EVALUABLE with the missing quantity named, never a digit for a non-value.

## Verified facts (read 2026-11-02)
**Code:**
- `MultipleTesting.Factor(N) = 1 + 0.25·log2 N`; `ITestLedger` (`AppendAsync(LedgerRow)`, `CountVariantsAsync(strategyId)`) keys families by `StrategyId` and counts distinct `StrategyHash` values; `LedgerRow` carries a Lab run id and key (P13-05 defines how a feature-set trial fills it: family `ext:<version>` in `StrategyId`, trial hash in `StrategyHash`, a deterministic run id, `Method` `extension-compare`).
- `WalkForwardValidator`, `HoldOut`, `EligibilityRule`, `EntryWindowLearner` exist; `Wilson`, `PairedComparison` exist (Assistant evaluation).
- `IEventFilterVerdict.NewestAsync(ruleVersion)` returns `EventFilterVerdict(State, RuleVersion, Reference)` or null; **no implementation** (null = unavailable); `EventFilterAutoDecision` and `ApplyEventFilterAutoHandler` exist; the snapshot rule version is `evf-1`; the switch holds `results_within_days` (default 2).
- The simulator's `EventUnavailable` fate does not split the reasons; ADR 0182 says P15-10 derives `no_upcoming_entry` from the snapshot.
- `research.extension_evaluations` does not exist; P13-05 creates it (with `protocol` and `inputs` columns so F, P and L records fit).

## Scope
**In:**
- Domain (pure): `ProtocolF`, `ProtocolX` (adapter over P13-05 with quarterly folds and season-cluster bootstrap), `ProtocolP`, `ProtocolL` (reads an eval run), `SeasonFolds`, `VerdictTable`, `EvidenceBlock` (P8-00 D3 shape).
- Application: `EvaluateExtensionHandler`, verb `xd learn extension-eval --family filter:results|ext:ev-1|ext:fu-1|ext:llm-1|pead`; `EventFilterVerdictReader` (implements `IEventFilterVerdict`); a call of `ApplyEventFilterAutoHandler` after each stored F verdict.
- `GET /api/research/extensions?family=` and `/{id}` (the P13 contract, shared).

**Out:** turning any feature into scoring (no P15 slice builds that; a KEEP is a record — owner decision 3); changing N or the approval mode of the filter; the screens (P15-11); new data; the depth extension `md-1`.

## Common rules
- **Seasons and folds.** A season is a calendar quarter of the decision date. Purge = hold + 1 bar, embargo 5 sessions; hold-out = the last 4 seasons with data, reserved with `HoldOut` and read once by `--holdout` after the test verdict is stored (the record says whether it was read). Season-cluster (block) bootstrap, 10,000 draws, seed 1337.
- **Ledger before results.** Each counted variant is appended to `lab.test_ledger` under its family **before** its numbers are computed; N = distinct trial hashes of the family; every margin × `Factor(N)`.
- **Gross R** unless the owner's checked charge sheet exists (ISS-25); the label says which.
- **Honest states.** A missing quantity is named (`seasons 8 of 20`, `skipped trades 63 of 100`); no value is printed as a digit when it is not one; `INCONCLUSIVE` and `NOT_EVALUABLE` are verdicts, not errors.
- **Record.** `research.extension_evaluations` row (insert-only): deterministic id (SHA-256 of the canonical inputs), family, protocol, versions (`ev-1`/`fu-1`/`llm-1`, `evf-1`, engine, parser version, lag, calendar SHA-256, filings watermark, eval run id), N, counts, estimates with intervals, verdict, limits text, evidence JSON, `holdout_read`.

## The protocols as built
**F — event-risk filter** (variants N ∈ {1, 2, 3}, Factor(3)). Population: the trades of stored, unfiltered research runs of all stored strategies (not runs with `not_event_day`). Each trade is classified at its decision instant from the run's snapshot: **skipped** (the window of N sessions would exclude it, or its position spans a results event within N sessions of entry), **taken** (outside), or **unavailable** (split by reason `no_entry`, `only_cancelled`, `no_upcoming_entry`). KEEP requires all of P15-00 F, with one correction: the "removes ≤ 50 %" rule counts what the filter **actually removes when ON** — skipped **plus all unavailable** (it fails closed). The report prints: shares skipped, taken, unavailable by reason, **the share of entries lost to `no_upcoming_entry`**, the tail test (share with R ≤ −1.5, Wilson), the mean-R difference with its bound, folds with the same sign. A verdict is stored per N; `EventFilterVerdictReader.NewestAsync(evf-1)` returns the newest verdict **for the N of the effective switch** (another N is no verdict: null).

**X — `ev-1`, `fu-1`, `llm-1`.** P13-05 paired comparison (A = `tf-1`, B = `tf-1` + set; same rows, folds, seeds) with **quarterly** folds and **season** resampling; KEEP needs ≥ 6 folds, ≥ 1,000 paired OOS candidates, lower bound ≥ `0.02 R × Factor(N)`, Δ log loss ≤ −0.005 × Factor(N), better in ≥ 2/3 of folds, calibration ≤ 5 pp; the intraday/swing split is reported. Extraction records must match the run and current versions (else NOT_EVALUABLE `features_stale`). `fu-1` with parser `xbrl-1-synthetic` → NOT_EVALUABLE `parser_unverified`. `llm-1` is evaluated only if the newest **accepted** eval run has set D `PASS` with the model and prompt hashes of the stored extractions; else NOT_EVALUABLE `protocol_L_not_passed`. Single-field quality filters go only through the entry-window pattern (ADR 0069), each bucket rule a counted trial.

**P — PEAD.** Variants (measure × hold) = 4, Factor(4); the choice is made on the first 8 seasons with `sue_yoy` computable and frozen in the record before the test seasons are read; test = the next 8; hold-out = the last 4. Top bucket against the middle three on the same dates; KEEP needs ≥ 8 test seasons, ≥ 250 top-bucket events, season-cluster lower bound of ΔR ≥ 0.075 R, positive in ≥ 2/3 of test seasons, hold-out same sign. Needs 20 bar seasons; today about 8 → NOT_EVALUABLE `seasons 8 of 20`.

**L — the flag.** Read, not recomputed: the newest accepted eval run's set D result (P15-08 grader); verdict KEEP iff it passed, DROP iff a gating run with ≥ 200 rows failed, NOT_EVALUABLE otherwise; the record keeps the eval run id and fingerprint.

## Acceptance criteria
1. **Placebo and planted effect.** For X and P: with random labels (no skill) the verdict is not KEEP at N = 1 and N = 16; with a planted effect it is KEEP at the required folds or seasons, and NOT_EVALUABLE with one fold or season fewer. For F: a planted gap-through tail in the skipped set gives KEEP on the tail test alone; a planted removal of 60 % gives DROP with the reason.
2. **`no_upcoming_entry` share.** On a fixture with a calendar where most past decisions fall after a meeting and before the next intimation, the F record prints each unavailable reason's share and the share lost to `no_upcoming_entry`, the 50 % rule counts them, and the verdict is DROP when skipped + unavailable > 50 % (hand table).
3. **Thresholds of NOT_EVALUABLE.** Fewer than 100 skipped trades (F), 6 folds or 1,000 candidates (X), 8 test seasons or 250 events (P), 200 labels (L) → NOT_EVALUABLE with the missing quantity named and no digit for a non-value (render test of the evidence JSON).
4. **Ledger first.** A test with a ledger fake that records call order shows every trial appended before its metric is computed; re-running the same evaluation appends nothing new and yields the same record id.
5. **Hold-out once.** The hold-out seasons are not read by `extension-eval` without `--holdout`; with it, they are read once per family and version, the record says so, and a second `--holdout` run is refused `extensions.holdout_used`.
6. **Event-filter verdict port.** With a stored F KEEP for N = 2 and the switch at N = 2, `NewestAsync("evf-1")` returns it with its reference; with the switch at N = 3 it returns null; with `approval = auto` the auto handler then records an `auto` row (P15-06 rules); with `manual` nothing changes. A DROP after an auto ON turns it OFF (recorded `auto`). Tests use the real reader over stored rows.
7. **Determinism.** Same stored inputs in any order → identical records and ids.
8. **API.** `GET /api/research/extensions?family=` lists records (newest first, at most 100); `/{id}` returns the record with its evidence block; 404 `extensions.not_found`; 400 `extensions.invalid_query` for an unknown family.
9. **No decision use beyond the port.** Architecture test: the protocol code and records are referenced by the verb, the endpoints, `EventFilterVerdictReader` and tests; nothing in selection, planning, the scorer or `XDrishti.Execution` reads them. No-order tripwire green.

## Contract
- **API:** `GET /api/research/extensions`, `/{id}` (`ApiJsonContext`; `scripts/gen-api.sh`). The plan's `/api/research/pead` is dropped: PEAD is the family `pead` of this list.
- **Data:** `research.extension_evaluations` (by P13-05's migration `AddExtensionTables`, written through `IExtensionEvaluationStore`), insert-only for `xd_app`, nothing for `xd_exec` or the read roles. No migration in this slice.
- **Verb:** `xd learn extension-eval --family … [--holdout]`.

## Non-functional
PEAD over 5 years ≤ 5 minutes (UNVERIFIED); X for 6 folds × 2 models over 5,000 rows ≤ 10 minutes; F over all stored runs ≤ 2 minutes.

## Test plan
- **Unit:** each protocol's verdict table, season folds and purge, season-cluster bootstrap on a hand case, the 50 % rule with unavailable, the port's N rule.
- **Property:** placebo/planted (criterion 1), determinism, ledger order.
- **Integration (Timescale):** the verb on a fixture database (tables from P13-05), the endpoints, the auto path end to end with `approval = auto`.
- **Architecture:** criterion 9.

## Decisions (researched)
1. **F counts unavailable as removed.** The filter fails closed (ADR 0182); a removal rule that ignored it would approve a filter that empties most of the book.
2. **F classifies unfiltered trades instead of comparing two runs.** One population, no second selection effect from the filter's own unavailable rule.
3. **The verdict is per N and the port answers for the switch's N.** The auto rule never changes N; a KEEP for another N must not switch this one.
4. **L is read from the eval run, not re-run.** One place grades the flag (P9-09); the record links to it.

## Risks and honest limits
- On the data held today F may be evaluable (if the owner's calendar covers the window) but X and P are NOT_EVALUABLE until CF-04; L waits for labels.
- The calendar's fail-closed rule likely makes F remove more than half of the trades, so DROP is a plausible first verdict; that is the honest answer for a filter that can only act between an intimation and its meeting.
- Seasons, not trades, are the independent units; intervals are wide.

## Open questions (owner)
1. ~~Specify and build P13-05 inside this slice?~~ Resolved 2026-11-02: P13-05 is its own slice (spec Draft), built before P15-05; this slice only uses it.
2. Use the hold-out now or wait for more seasons? **Default: wait; `--holdout` is an explicit act.**
3. After a KEEP for `ev-1`, `fu-1` or `llm-1`, should a scoring switch be built (the P13-07 pattern: default OFF, fresh password, auto option recorded as auto)? **Default: no slice builds it; a KEEP is a record only.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer (Opus): no blockers/majors (told to look for any metric computed before its ledger row, any non-value printed as a digit, any hold-out read without the flag, any auto ON without a KEEP for the switch's N)
- [ ] No migration (P13-05 owns the table); contract regenerated (`scripts/gen-api.sh`)
- [ ] ADR written; doc 20 §D (verdicts), doc 09 updated
