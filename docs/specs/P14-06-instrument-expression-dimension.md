# P14-06 — Instrument-expression dimension: an `Expression` type, expression profiles as content-addressed research rows, and an optional eighth cell-key part that leaves every stored key and every decision path unchanged

- **Status:** Draft (written 2026-11-03; the owner's Approved is needed before an agent builds it)
- **Phase / roadmap row:** P14 — slice P14-06 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Instrument-expression dimension"); phase acceptance criterion 8.
- **Design refs:** docs/design/09 §3 layer 1 (cells), §14 · 07 (expression) · 20 §C · P14-00 D4 (the expression is not a strategy-file field), D7 · [BK-11](BK-11-universe-cell-dimension-and-strategy-universe-link.md) decision 5 (`universe` is the 7th part, `expression` the 8th) · ADR 0058 (run identity), 0064 (cells), 0045 (insert-only research rows). New ADR: **next free number at build time** (design change of doc 09: the eighth part and its canonical form).
- **Needs:** P6-01 (built: `CellId`, `research.cell_stats`). BK-11 is **not needed** (Draft, not built): this slice reserves its seventh position with the single value `*` and BK-11 later widens the allowed values. **Needed by:** P14-08 (profiles, the expression of a re-marked trade), P14-09, P14-10, P14-13, P14-15 (cells and families per expression), P14-16 (reports).
- **Size:** 2.5 agent-days (estimate).
- **Safety:** no for trading — and that is the rule this slice must prove: selection, planning, paper, live and execution read **only `cash` cells**, so a stored option or pair cell changes no plan, ticket or order. No order path, no setting.

## Goal
The learning engine can name and store a cell such as "ORB, 15m, BUY, intraday, FIXED_RR, any regime, any universe, as an at-the-money option" next to the cash cell of the same setup, from research rows, while every stored six-part key still reads as `cash` and no screen, report or plan changes until an option or pair cell is actually written.

## Verified facts (read 2026-11-03 in `backend/src`)
- `CellId(Strategy, Timeframe, Side, Style, Exit, Regime = "*")` (Domain/Learning): `Key` joins **six** parts with `|`; `TryParse` accepts exactly six; each part ≤ 64 characters (`MaxPartLength`), no `|`. It has **no `Universe`** part: BK-11 is a Draft.
- `CellId` is read by `CellStatsStore`, `CellStatsRow`, `CellInput`, `MetaPolicyEvaluator`, `PlanCandidate` (Domain/Selection), `TicketReplayer` and `ITicketSource` (Planning), `CapturePaperFillsHandler` and `ConfigSnapshotSource` (Paper), and the learning handlers (`ComputeCellStatsHandler`, `GetCellStateHandler`, `ProposeHypothesisHandler`, `ProposeEntryWindowsHandler`, `RecordCellTransitionHandler`, `ReplayDriftHandler`, `AnalyzeLossTagsHandler`, `CellSourceReader`, `TrainingDataLoader`).
- `research.cell_stats.cell_key` is `text` (indexed, unique with `snapshot_key, window_code`); `lab.hypotheses.cell_key` is `varchar(400)`. An eight-part key of 64-character parts is at most 519 characters: the hypotheses column would refuse the longest keys.
- No `Expression` type, profile table or expression column exists anywhere.

## Scope
**In:**
- Domain: `Expression` (closed set, stored by name: `cash`, `future`, `option_buy`, `debit_spread`, `credit_spread`, `iron_condor`, `pair`, `basis`); `CellId` gains `Universe = "*"` and `Expression = Expression.Cash`; `ExpressionProfile` (kind + canonical parameters → canonical text → SHA-256 `profile_hash`), with `ExpressionProfile.Cash` as the identity profile.
- Data: migration `AddExpressionProfiles`: `lab.expression_profiles`; `lab.hypotheses.cell_key` widened to `varchar(600)` (no data change).
- Application: the cell-statistics source carries the expression of each trade (`cash` for every simulator trade; P14-08 supplies option rows later); `IExpressionProfileStore` (append, get by hash, list).
- API DTOs: every response that returns a cell key gains `expression` and `universe` fields (additive; `scripts/gen-api.sh`).
- The decision-path guard (criterion 6), tests, ADR, doc 09 §3 and §14.

**Out:** a strategy-file field (D4: strategy files, their schema, hashes and the oracle do not change); the universe tiers (BK-11); any re-marked trade (P14-08); a screen (P14-16); any non-cash use in selection or plans.

## The rules
- **Canonical key text.** Parts in order `strategy|tf|side|style|exit|regime|universe|expression`, written with the **fewest trailing parts**: universe `*` and expression `cash` → six parts (today's text, byte for byte); a universe other than `*` and `cash` → seven parts (BK-11); any expression other than `cash` → eight parts, universe written explicitly. Exactly one text per cell.
- **Parsing.** `TryParse` accepts 6, 7 or 8 parts. A seventh part must be a known universe (in this slice only `*`; BK-11 adds `liq_high|liq_mid|liq_low`). An eighth part must be a known expression other than `cash`. A seven-part key whose universe is `*`, or an eight-part key whose expression is `cash`, is **not canonical** and is refused (`null`), so two texts can never name one cell. Anything else is refused as bad six-part keys are today.
- **Profiles.** An expression profile is the pre-registered definition of how a cash signal becomes the expression: kind plus parameters (for `option_buy`: strike rule, expiry rule, exit rule, premium stop, liquidity filter; P14-08 owns the parameter list per kind). The canonical text is `expression-profile/1`, the kind and the parameters sorted by name with invariant number formatting; `profile_hash` = SHA-256 lower hex. A profile is part of the identity of every re-mark run (P14-08) and of every ledger trial (P14-09, P14-13, P14-15), never of a strategy version.
- **Cells from trades.** A cell's expression is the expression of the trades it is computed from. Simulator trades are `cash`. A cell set never mixes expressions.
- **Decision paths read cash only.** Selection (`PlanCandidate` and its sources), planning (`TicketReplayer`, `ITicketSource`), paper (`CapturePaperFillsHandler`, `ConfigSnapshotSource`), live and execution take only cells whose expression is `cash`; a non-cash row is skipped and counted (`non_cash_cell_skipped`), never an error and never a candidate.

## Acceptance criteria
1. **Round trip.** `ORB|15m|BUY|intraday|FIXED_RR|*` parses with `Universe "*"`, `Expression cash` and `Key` returns the same six-part text; `ORB|15m|BUY|intraday|FIXED_RR|*|*|option_buy` parses and returns the same eight-part text; `…|*|*|cash` and `…|*|*` (seven parts with `*`) are refused; `…|liq_high` is refused until BK-11; `…|*|*|option_sell` and a nine-part key are refused.
2. **Stored keys unchanged.** A golden list of every key shape stored in a fixture `research.cell_stats` and `lab.hypotheses` reads back with `cash` and writes back byte-identical; no migration rewrites a key.
3. **Profiles.** Two profiles with the same parameters in a different order have the same `profile_hash`; any parameter change changes it; `ExpressionProfile.Cash` has a fixed hash pinned by a test; a stored profile is never updated (insert-only; a second insert of the same hash writes nothing).
4. **Migration.** `AddExpressionProfiles` creates `lab.expression_profiles(profile_hash char(64) PK, kind varchar(16) in the closed set, params jsonb, canonical_text text, created_at default now())` and widens `lab.hypotheses.cell_key` to `varchar(600)`; `xd_app` has SELECT and INSERT only on the new table (grants test); `xd_exec`, `xd_mcp_read`, `xd_mcp_sql` have nothing on it (`ExecGrantsTests`, `ReadOnlyRolesTests` extended); `Down` drops the table and narrows the column only when no key is longer than 400.
5. **Reports and API unchanged for cash.** Every existing report golden and API snapshot is unchanged except the two additive fields (`expression: "cash"`, `universe: "*"`); `scripts/gen-api.sh` regenerated.
6. **No decision use of a non-cash cell.** With a fixture holding an `option_buy` cell whose statistics beat the cash cell, the plan run, the ticket replay, paper capture and the selection candidates are byte-identical to the run without it, and `non_cash_cell_skipped` counts it. An architecture test pins that `Expression` values other than `Cash` appear only in Domain/Learning, Domain/Options, the research and report code, the stores and tests.
7. **Lengths.** An eight-part key with 64-character parts round-trips through `research.cell_stats` and `lab.hypotheses`.

## Contract
- **API:** additive fields `expression`, `universe` on cell DTOs; no new route.
- **Data:** `AddExpressionProfiles` (criterion 4).
- **Settings, jobs, UI:** none.

## Non-functional
`TryParse` and `Key` cost no more than 10 % over today (benchmark test, not gated); no change to the cell-statistics run time for cash cells.

## Test plan
- **Unit:** criteria 1 and 3; canonical-form refusals; part-length limits with the new parts.
- **Integration (Timescale):** criterion 2 on fixture rows, criterion 4 grants and `Down`, criterion 7.
- **Application:** criterion 6 with fakes and the real selection and planning handlers over a fixture.
- **Architecture:** criterion 6's pin; `Expression` not referenced by `XDrishti.Execution`.

## Decisions (researched)
1. **Fewest-trailing-parts canonical form.** Keeps every stored key and every golden byte-identical and gives one text per cell; the alternative (always write eight parts) would rewrite every stored key and every report.
2. **Reserve the universe position now with `*` only.** BK-11 and P14 agree on the order (BK-11 decision 5); taking the 8th position without the 7th would make an eight-part key ambiguous when BK-11 lands. BK-11 then only widens a value list.
3. **Profiles as research rows, not strategy fields** (P14-00 D4): no strategy hash, schema, oracle or stored version changes.
4. **Cash-only on decision paths, enforced by test.** P14 is research only; a KEEP enables research and paper discussion, never a plan (P14-00 risk 8).

## Risks and honest limits
- BK-11 must keep this canonical rule (it is written into its decision 5); if BK-11 is changed to always write seven parts, the stored keys of this slice still parse, but the canonical text would change — the ADR records the dependency.
- Cells per expression multiply the cell count by the number of profiles; P14-09 counts every profile in N.

## Open questions (owner)
None blocking. Default: `cash` only on every decision path until a later slice (P12 order groups at the earliest) is specified and approved.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any stored key that changes, any decision path that can read a non-cash cell, any second text for one cell)
- [ ] Migration `AddExpressionProfiles` with `Down`; contract regenerated (`scripts/gen-api.sh`)
- [ ] ADR written; doc 09 §3 and §14 updated; BK-11's spec notes that the seventh position is reserved
