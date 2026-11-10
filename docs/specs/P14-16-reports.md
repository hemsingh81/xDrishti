# P14-16 — Reports: read-only summaries of the option archive, the expression runs, the pair formations and every P14 verdict, in the P8 honest states, as API and CLI first; the screen only when there is something to show

- **Status:** Built 2026-11-04 to 2026-11-09 (ADR 0201). Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-16 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Reports: coverage, expression comparison, pairs, with P8 states and evidence").
- **Design refs:** docs/design/13 (reports and UI) · P8-00 D3 (evidence block, six honest states, no digit for a non-value) · ADR 0099 (report engine core), 0178 (coverage route), 0183/0186 (records and the shared route) · P15-11 (the pattern for verdict cards) · prototype: **no screen in `docs/prototype/` shows options, expressions or pairs** (to be confirmed at build by a search; P15-11 found the same for filings).
- **Needs:** P14-04 (built: `GET /api/options/coverage`), P14-08, P14-09, P14-10, P14-13 (their tables and records); the shared `GET /api/research/extensions` (built). Each part works with what exists when it is built; a missing table is a hidden section, never a fake value.
- **Size:** 2 agent-days for part A (the plan said 3 for both parts); part B 1.5 agent-days when triggered.
- **Safety:** no — read only; returns aggregates, never a price series, IV series or Greek series; no owner act, no setting.

## Goal
The owner can see, from one command or one route, what the option archive holds and where it has holes, what each expression and pair run paired and lost and why, and every P14 verdict with its evidence and missing quantities — without any screen code being written before there is a verdict worth showing.

## Verified facts (read 2026-11-03)
- `GET /api/options/coverage?underlying&from&to` exists (P14-04, ADR 0178: per session checks, usable flags, `usableChainSessions`, no price); `GET/PUT /api/options/chain-recorder` and `/api/options/backfill` exist (P14-02, P14-03: switch, history, progress).
- `GET /api/research/extensions?family=` and `/{id}` exist (P15-10) and are extended by P14-09 to the expression, pairs and basis families.
- The frontend has `features/learning`, `features/reports`, `features/quality`, `features/services` and `shared/analytics` (`EvidenceBlock`, `MetricTile`, `DataTable`); no frontend code calls an options route today.
- P14-02 left `GET /api/options/chain?underlying&expiry&at` (P14-00 contract) to this slice.

## Scope
**Part A (build now):**
- Application read models: `OptionArchiveSummary` (per underlying: recorded sessions, usable sessions, gaps by outcome, backfill keys done/empty/given up, first and last date, the spread model state), `ExpressionRunSummary` (per expression run: profile, pairs, unpaired by reason, fallback share, `expiry: derived` share, `spread_from_recent_archive` share, lot feasibility shares), `PairFormationSummary` (per quarter: candidates, tested, unavailable, selected, BH threshold, half-life distribution), `P14VerdictList` (the newest record per P14 family with its verdict, missing quantities and limits).
- Routes: `GET /api/options/summary`, `GET /api/research/expression-runs?profile=&limit=`, `GET /api/research/expression-runs/{id}`, `GET /api/research/pair-formations?from&to`.
- CLI: `xd reports options [--json]` printing the four summaries in P8 text form.
- Tests; doc 13 note.

**Part B (frontend, deferred):** an "Options and stat-arb" section in Learning (verdict cards with the evidence block, as P15-11) and an "Options archive" card on Data quality. **Built only when the owner asks, or when the first P14 record that is not NOT_EVALUABLE is stored**; its acceptance criteria are those of P15-11 criteria 1, 6 and 7 applied to these routes. No frontend code is written in part A.

**Out:** the chain-rows route of P14-00 (`/api/options/chain`): **dropped** — no report needs raw snapshot rows, and a route returning them would be the first non-quality reader of the archive on the API; any chart of prices or IV; switches (the P14-02/03 routes exist; a Services-page switch is a separate small slice the owner already deferred).

## Acceptance criteria
1. **Honest states.** Each summary field that has no source is `unavailable` with its reason word (`not_recorded`, `no_model`, `no_runs`, `cut`), never `0` or empty; the JSON has the text `unavailable`, never a number (render and snapshot tests).
2. **No series.** A test pins the field names of every route: no price, IV, Greek, bar or quote field; only counts, shares, dates, verdict words, bounds and margins from records.
3. **Verdict list.** With fixture records for `expr:option_buy` (NOT_EVALUABLE `folds 2 of 6`), `pairs:intraday-1` (INCONCLUSIVE) and `ext:iv-1` (none), the list shows the two records with their missing quantities and `none stored` for the third; cut families (from P14-17's `--cut` list or a setting) show `cut`.
4. **Errors.** 400 `options.invalid_query` for an inverted or over-400-day range, an unknown profile hash shape or a limit above 100; 404 `research.not_found` for an unknown run id; an empty list (not an error) when nothing is stored.
5. **Read only and bounded.** Architecture test: the handlers use read ports only; every query is bounded by a date range or a limit; nothing references `XDrishti.Execution`, selection or planning.
6. **CLI.** `xd reports options` prints the same figures as the routes (snapshot test) with the limits block always present; exit 0.
7. **Contract.** `ApiJsonContext` entries and `scripts/gen-api.sh` regenerated; no frontend file changes in part A (a test or review check: the diff touches no `frontend/src` file other than the generated schema).

## Contract
- **API:** the four routes above, problem codes `options.invalid_query`, `research.not_found`.
- **Verb:** `xd reports options [--json]`.
- **Data, settings:** none.
- **UI:** part B only, deferred.

## Non-functional
Each route ≤ 300 ms p95 on the owner's database (bounded reads of summary rows and records); the archive summary reads `options.quality_results`, `options.chain_sessions` and `options.fetch_log` aggregates only (the per-table reader pins are extended by exactly the summary reader, deliberately, as ADR 0178 did).

## Test plan
- **Integration (Timescale):** each route on fixture rows and on an empty database; the error cases; snapshot of the CLI.
- **Unit:** the read-model mappers' state words.
- **Architecture:** criteria 2 and 5.

## Decisions (researched)
1. **API and CLI first, screen later.** Every P14 verdict is expected to be NOT_EVALUABLE for months (CF-04, 60 sessions, the charge lines); a screen built now would show only missing quantities. The CLI and routes answer the same question at a fraction of the cost.
2. **Drop the chain-rows route.** No report needs it; it would widen the archive's readers for nothing.

## Risks and honest limits
- Part B may never be needed if every family is dropped or cut; that is the kill criterion working.

## Open questions (owner)
1. Build part B now rather than on the first non-NOT_EVALUABLE record? **Default: deferred.**

## Done when
- [ ] All acceptance criteria have evidence (part A)
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any series field, any non-value printed as a digit, any unbounded query)
- [ ] Contract regenerated (`scripts/gen-api.sh`); no migration
- [ ] Doc 13 note

## Part B build note (2026-11-09, approved by delegation)
Built on the owner's delegation, with the existing routes only (no backend change). Decisions taken as best practice:
- Learning gets one lazy tab "Options and stat-arb": verdict cards (the P15-11 `ExtensionCard` with the evidence block) for `expr:option_buy`, `expr:debit_spread`, `expr:credit_spread`, `expr:iron_condor`, `pairs:intraday-1`, `ext:iv-1`; "none stored" when a family has no record; then expression runs (newest 20) and pair formations as tables. The BASIS family is not shown (default cut, no stored record); add it to `p14Families` when it is built.
- Data quality: the existing lazy "Options archive" card gains the `GET /api/options/summary` table above the coverage list. No new eager code beyond one tab registry entry.
- The verdict words KEEP, DROP, INCONCLUSIVE and NOT EVALUABLE are shown as stored (a KEEP is not a pass of anything unavailable: a missing bound reads as words, never a digit); an unavailable share, count, date, gap or half-life is a word with its reason (not a digit); non-finite numbers are never printed (`reportedMetric`). No price, IV or Greek field is read.
- Gap: the `P14VerdictList` read model has no route, so cut families cannot show `cut`; the section uses `GET /api/research/extensions?family=` per family (an empty list reads "none stored"). Not a blocker.
- Cuts: this screen cannot know which families are cut (no route carries it); `xd options phase-check --cut` is the authority. Each family notes that a development KEEP changes nothing and whether the hold-out has been read. Lot feasibility is shown per run.
- Prototype: no screen shows options, expressions or pairs; the layout follows the Learning panels.
