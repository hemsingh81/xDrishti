# P10-03 — Weekly review and the discipline report (R19), deterministic

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P10 — Paper trading, slice P10-03 of [P10-00](P10-00-paper-trading-plan.md)
- **Design refs:** docs/design/13 §2 (R19 Discipline, R26 Decisions review, R32 Execution gap), 19 §3 (skipping/adding trades), 09 §9 (execution feedback, "your overrides"), 09 §13, 15 §1; ADR 0090 (decision groups), 0093 (journal, discretionary fills), 0094 (weekly digest pattern), 0099 (report states); P7-13, P7-14, P7-15, P8-00 D1/D3/D10
- **Needs:** P10-01, P10-02; P7-13, P7-14, P7-15 (built). ADR: "the weekly paper review and the discipline report" (next free ADR number at build time)
- **Size:** 5 agent-days
- **Safety:** no. Read endpoints, one stored text per week and a paused service that writes only that text; no setting, ticket, approval or order is touched. (The service starts paused; switching it on is the owner's act.)

## Goal
Once a week the owner can read one deterministic review of the paper epoch (where it stands, what happened, what was skipped, added or resized and what that cost in R, what changed in the configuration) and the discipline report behind it, every number with its n and state, nothing applied and no composite score.

## Scope
**In:** pure `Domain/Paper` `DisciplineClassifier`, `DisciplineReport`, `DisciplineLine`, `ExecutionGap`, `PaperReviewBuilder`, `PaperReviewWeek`, `ReviewText`; `Application/Paper` `BuildPaperReviewHandler`, `GetPaperReviewHandler`, `ListPaperReviewsHandler`, `GetDisciplineHandler`, `PaperReviewService` (id `paper-review`); port `IPaperReviewStore`; Infrastructure store, migration `AddPaperReviews` (`paper.weekly_reviews`, insert-only, with `Down`); endpoints below; verbs `xd paper review [--week YYYY-Www] [--store]`, `xd paper discipline [--from] [--to]`; frontend tabs **Review** and **Discipline** in the `/paper` frame of P10-02; `scripts/gen-api.sh`.
**Out:** an owner "mark reviewed" write, push or e-mail of the review, the backtest comparison itself (P10-04, summarised by one line), grading discretionary trades in R (no plan levels exist for them), any change of configuration, an LLM-written text (P9-06 may later phrase this text; the numbers stay these).

## Acceptance criteria
1. **Hand-checked discipline.** Given the fixture below, then each category count and each R figure equals the hand number, and the deviation lines add as stated.
2. **Count-once.** A ticket sized for two paper accounts is one ticket in every count and one R; only rupee lines are per account.
3. **Categories are exclusive and exhaustive** for primary-group tickets of the window: `followed` (approved at full size, entered and closed), `sized_down` (approved below the proposed size), `approved_not_entered` (approved at any size but no entry: the trigger never came, with its skip code from capture, e.g. `not_triggered`, `entered_before_armed`, `no_bars`), `pending` (approved and armed, entry or exit outcome not yet captured, or entered and still open), `rejected`, `lapsed`, `invalidated_preopen`; `confirmed_despite` is a flag on followed/sized_down, counted beside, not instead, plus the separate groups `reserve_approved` and `auto_approved`. A property test: every primary ticket falls in exactly one of the seven; the seven sum to the primary count.
4. **Skipped is graded by the plan-price grade.** For `rejected`, `lapsed` and `invalidated_preopen` tickets the R is the stored P7-13 grade (`simulated on plan prices`); a not-triggered grade is counted as `not_triggered`, a not-gradable or missing grade as `not_graded` (never R 0). A mean R needs at least 15 graded tickets in the line, else `insufficient`; the count is always shown.
5. **Sized down.** `sizeEffectR = (qty_allocated - qty) / qty_allocated x r_sim` for a closed paper trade; the line sums it; an open or not-triggered ticket adds nothing and is counted apart.
6. **Added.** Discretionary fills of Trading accounts for the window (P7-14 `DiscretionaryAsync(DateOnly)` takes one day, so the handler makes one call per session of the window inside one handler, merging the results) are counted (fills, symbols); their R is `unavailable: no plan levels` and their rupees are shown only when the journal holds a finished round trip (else `unavailable`). With no real fills the line reads `empty`, not zero.
7. **Execution gap for paper.** The waterfall rows are plan R (mean `r_exact`), modelled slippage (mean `r_gross - r_exact`), charges (mean `r_gross - r_net`, `unavailable` without a sheet), net; missed triggers and real slippage are `not_evaluable: paper has no real fills (P11)`. The overrides are the discipline lines, not part of this waterfall.
8. **The review text is deterministic**: the same stored records give the same text byte for byte; it opens with the pinned lines `Paper only: no order was placed and none exists.` and `Nothing was changed: this review applies nothing; changing any setting is your own act.`; every section ends with its limits sentence; the database refuses a stored text without the first pinned line.
9. **Storing.** One review per ISO week and epoch, insert-only; a second build returns the stored one and writes nothing; storing is allowed only for the current India week at or after Saturday 10:00 IST (the service runs 10:30) or for the most recent completed week within 7 days; any older week is a dry run whose text says `State as of <build date>, not as of the end of the week`. A future week is refused (`paper.invalid`).
10. **Pace and ETA honest.** Counted n, days elapsed of 84, and the weekly pace; an ETA to 60 only with n >= 10 counted trades and at least 3 weeks elapsed, else `unavailable`; the text says an ETA is a straight-line guess.
11. **Empty and failure cases.** No epoch: the review says so and nothing else; no trade yet: sections show `empty` words, never zeros; a source that cannot be read (P10-04, P10-05 not built or failing) shows that section `unavailable: <reason>` and the rest is produced.
12. **Nothing applied.** A spy shows the builder calls reads only (no plan, approval, setting, hypothesis or broker port); the stored text is the only write; `xd_app` can select and insert `paper.weekly_reviews`, not update or delete.
13. UI: the tabs render the stored or dry-run text as sections with the tables (closed trades, discipline lines); loading, error, empty; no write control; a 360 px layout without horizontal page scroll; the discipline table has a text alternative to any chart.

## Contract
- **API (read only):** `GET /api/paper/reviews?epoch=&limit=` -> list (week, built at, epoch); `GET /api/paper/reviews/{isoWeek}?epoch=` -> `PaperReviewView` { `week`, `epochId`, `builtAt`, `stored`, `text`, `sections[]` { `id`, `title`, `lines[]`, `metrics[]`, `limits` } }; `GET /api/paper/review?week=` -> a dry-run build (not stored); `GET /api/paper/discipline?epoch=&from=&to=` -> `DisciplineView` { `lines[]` { `category`, `n`, `rMetric`, `rupees`, `note` }, `execution` [rows], `evidence`, `limits` }. 422 `paper.invalid`, 404 `paper.not_found`.
- **CLI:** `xd paper review` prints (default) or stores (`--store`, with the storing rule); `xd paper discipline` prints; exit codes as `xd plan digest`.
- **Data (migration `AddPaperReviews`):** `paper.weekly_reviews`: `id bigserial`, `iso_week text check ~ '^[0-9]{4}-W[0-9]{2}$'`, `epoch_id bigint fk`, `built_at timestamptz`, `built_by text`, `text text check (text like 'Paper only: no order was placed and none exists.%')`, `sections jsonb`, unique `(iso_week, epoch_id)`; insert-only via `MakeAppendOnlyForAppRole`.
- **Service:** `paper-review`, weekly Saturday 10:30 India, `MustFollow = [weekly]`, starts paused. Log ids 5220 to 5229.

## Non-functional
Reads one epoch's entries, exits, skips, newest 60 plans with trails and approvals, the stored grades and the journal for the window; no replay, no computation of cell statistics. p95 < 800 ms for a 12-week epoch on the fixture. No secret; logs carry ids and counts only.

## Test plan
Unit: `DisciplineClassifier` table (every ticket trail shape), count-once, minimum-n states, sized-down arithmetic, execution waterfall, text determinism (golden file), storing-window rule, ETA rule. Application with fakes: sources unavailable, spy for reads only. Integration: migration, check on the pinned line, idempotent store, grants, endpoints on a seeded epoch. Frontend (MSW): tabs, empty and failed sections, no mutation import. Property: permuting input order does not change the report.
**Hand-worked fixture** (one week, 10 primary tickets, one paper account): T1 to T6 approved at full size and entered, results +2.0, -1.0, -1.0, +0.5, -1.0, +1.5 (sum +1.0R, 3 wins of 6); T7 approved, modified 100 to 60, entered, +2.0; T8 rejected, grade +1.5; T9 rejected, grade not triggered; T10 lapsed, grade -1.0. Counts: followed 6, sized_down 1, approved_not_entered 0, pending 0, rejected 2, lapsed 1, invalidated 0 (sum 10); the fixture is extended with T11 (approved, never triggered, skip code `not_triggered`) and T12 (approved, armed, capture not yet run) as a second block: approved_not_entered 1, pending 1, sum 12 = the primary count. `sizeEffectR(T7) = (100 - 60) / 100 x 2.0 = +0.8`. Skipped, graded: T8 and T10 = 2 tickets, sum +1.5 - 1.0 = +0.5R (mean `insufficient`, n 2 < 15); T9 `not_triggered` 1. Deviation lines printed separately: skipped +0.5R, size +0.8R; their sum +1.3R is printed as "R not taken (positive: leaving it cost you; negative: it saved you)" with n and `insufficient`. Taken total = 6 trades + T7 = 7 closed paper trades, sum +3.0R (1.0 + 2.0). Waterfall for one trade: plan 2.0000 -> slippage -0.0455 -> charges -0.0455 (total cost 5.00 on risk 110.00) -> net 1.9091.

## Decisions (researched)
1. **Q: What is "skipped" in a paper phase, where no real order exists to skip?** A: a primary ticket the owner rejected or let lapse (or the system dropped at pre-open, shown apart because it is not the owner's act). Its counterfactual is the P7-13 grade, which already replays every ticket whatever its decision. Why: that is the only honest measure of "cost of deviations" doc 13 R19 asks for, it needs no new data, and it uses the same simulator. Rejected: asking the owner to log "would have taken" by hand (hindsight risk, work).
2. **Q: "Added" trades?** A: real fills with no ticket from the P7-14 journal, counted, not graded. Why: a discretionary trade has no plan stop, so an R would be invented; P7-14 already decided this. In a pure paper epoch the line is `empty`.
3. **Q: A discipline score?** A: none. Doc 13/09 mention one; no definition exists and P8-00 D10 forbids inventing one. The report gives the lines with n. Source: P8-00 D10.
4. **Q: Minimum n for a mean?** A: 15 (the insight minimum of doc 15 §6), with 30 stated for any conclusion and a note under 30. Source: P8-00 D3, ADR 0090.
5. **Q: Stored or recomputed?** A: both: a dry-run build any time, and one stored text per week and epoch so the owner can see what was said then. Same pattern and rule as the digest (ADR 0094), including why a past week cannot be stored from today's state.
6. **Q: Text by an LLM?** A: no; deterministic templates. The assistant (P9-06) may phrase it later over these numbers; "numbers only from tools" (doc 19 §3).
7. **Q: What does the owner have to do about a review?** A: read it (15 minutes); there is no acknowledgement write in this slice (a write from the UI is a SAFETY slice and adds little: a skipped review shows as a gap in the weekly routine of P10-00).

## Risks and honest limits
- The counterfactual of a skipped ticket is a modelled grade, not a fill; at 10 to 20 skipped tickets in an epoch the lines are `insufficient` for a mean and say so.
- `r_sim` is size-free, so a size-down changes rupees, not R; `sizeEffectR` expresses it in planned-size R units, which is an accounting convenience, not a risk measure.
- Discretionary real fills appear only if the owner trades in a broker account the sync reads.
- A review built before the capture ran for a session shows that session as pending and says so.
- Review pace figures are a straight line; trading is lumpy.

## Open questions
None. Owner act later (default paused): switch the `paper-review` service on.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddPaperReviews` added with `Down`
- [ ] ADR written; doc 13 R19/R32 "as built" note and roadmap row updated; `scripts/build-docs.sh`

## Review fixes 2026-10-25 (2)
- AC3: `followed` defined (approved at full size, entered, closed); added `approved_not_entered` (with skip codes) and `pending`; the seven categories sum to the primary count; fixture extended (T11, T12).
- AC6: `DiscretionaryAsync(DateOnly)` takes one day (verified in `JournalStore.cs`): one call per session of the window in one handler.
- Size 3 -> 5 agent-days (P10-00 table to match).
