# BK-03 — Derived baskets refresh job and scores

- **Status:** Draft (written 2026-10-25 under the owner's delegation; decisions below researched; owner's Approved needed before build)
- **Phase / roadmap row:** BK — system baskets, slice BK-03 of [BK-00](BK-00-system-baskets-plan.md)
- **Design refs:** docs/design/06-instruments-and-baskets.md §3.1, §3.4 · docs/design/05 (EOD aggregation, calendar) · ISS-22 · research [system-baskets-nse](../research/system-baskets-nse.md) §2, §3.2, §3.3
- **Needs:** [BK-01](BK-01-index-basket-catalogue-fetcher-and-versioned-membership.md) (columns `kind`, `system`, `provenance`, protection), [BK-02](BK-02-derived-baskets-engine-pure.md) (the engine); built: `bars_agg` daily bars, the trading calendar, the services framework.
- **Size:** 4 agent-days (migration and store 1, universe builder and handler 1.5, job, intraday freeze and backfill 1, tests and ADR 0.5)
- **Safety:** yes (a scheduled job writing rows). Reads our own bars only; **no network**; `Baskets:System:Derived:Enabled` defaults **OFF**; writes only system-basket tables; never an order, never the trading configuration.

## Goal
Each enabled derived basket is recomputed on its cadence after the end-of-day bars land, stored as an immutable dated version with its parameters, members, scores and named exclusions, so a simulation or report reads exactly what was published on any past date.

## Scope
**In:** migration (`basket_versions`, `basket_member_scores`, derived rows in `basket_members`), universe builder `U(D)`, the `derived-baskets` scheduled service (daily, weekly, monthly, and intraday freeze at 09:20 and 09:30), version writer (membership windows), optional owner-triggered backfill, owner-editable parameter sets (new version of the params), read API for versions and scores, tests and ADR.
**Out:** the engine formulas (BK-02); the screen (BK-04); trading use of baskets (BK-06+); auto re-testing on a change; a setting ever enabling orders.

## Acceptance criteria
1. Given `Baskets:System:Derived:Enabled` OFF (default), when the service runs, then nothing is read or written and the run records `skipped: disabled`.
2. Given the daily EOD aggregation finished for Friday 2026-10-23 and basket "Most liquid (top 100)" enabled, when the job runs, then one `basket_versions` row exists with `as_of = 2026-10-23`, `effective_from = 2026-10-26` (next session per the trading calendar, Saturday and Sunday skipped), `params_hash`, `universe_size`, `member_count = 100`, and 100 `basket_members` rows with `valid_from = 2026-10-26`, `provenance = derived`.
3. Given the previous version's members, when a new version is written for `effective_from = 2026-10-27` in which 3 instruments leave and 3 enter, then exactly those 3 open rows get `valid_to = 2026-10-27`, 3 new rows `valid_from = 2026-10-27`, the other 97 rows are untouched; `members(asOf = 2026-10-26)` returns the old 100 and `members(asOf = 2026-10-27)` the new 100.
4. Given the job runs twice for the same `as_of` and the same `params_hash`, then the second run writes nothing (idempotent, outcome `unchanged`); given the same `as_of` with a changed parameter set, a new version is written with the new hash and the older version stays readable.
5. Given a Friday that is a market holiday (weekly basket), then `as_of` is the previous trading day and `effective_from` the next session after the holiday, from the calendar, never a fixed weekday offset.
6. Given the intraday basket "Gappers" and `Gappers` enabled, when the live bar at 09:20 IST has closed on a trading day, then a version is written once with `as_of` = that date, a `formed_at` of 09:20 and frozen: a later run the same day writes nothing; members use only bars before 09:20 (the engine's no-look-ahead property); if 1m bars are missing for the window the version is `not_formed` with the reason.
7. Given `basket_member_scores`, then each version stores `(instrument_id, score, rank)` for every ranked instrument in the version (members and the next 20 non-members by rank) so the buffer rule can read the previous rank and the UI can show "why"; a test shows the next run's incumbents are taken from the previous version's ranks.
8. Given the universe `U(D)`, then it is built from instruments with a daily bar on `as_of` (including a later-retired instrument that had bars that day) and not from today's tracked set; an instrument retired before `as_of` with no bar that day is absent. A test seeds a retired-after-D instrument and sees it in the version for D.
9. Given an instrument in the universe with fewer bars than the basket's minimum, then it is not a member and appears in the version's `excluded` json with `insufficient_history`; counts: members + below-threshold + excluded = `universe_size` (shown in the version row).
10. Given a backfill request (owner act, step-up) for derived basket X from 2026-01-01 to 2026-06-30, when run, then one version per due date is computed from bars cut at each `as_of`, all with `provenance = recomputed`, none overwriting an existing `derived` version of the same `as_of`; the response states the adjustments are today's restated series.
11. Given a derived basket, then every change route (PUT, DELETE, members, order, transfer as target or `move` source) is 409 `baskets.system`; copy out is allowed.
12. Given a job failure midway (exception after computing, before commit), then no partial version exists (one transaction per version) and the previous membership is still served; the run row shows `failed` with the error text.
13. Given a successful version with membership changes, then `BasketMembershipChanged` is audited with counts only; no approval is requested (informational).

## Contract
- **Data / migration** `AddDerivedBasketVersions` (builds on BK-01's migration):
  - `market.basket_versions(id uuid pk, basket_id fk, as_of date, effective_from date, formed_at timestamptz null, params jsonb, params_hash text, universe_size int, member_count int, excluded jsonb, outcome text check in ('applied','unchanged','not_formed','failed'), created_at, provenance text)`; unique `(basket_id, as_of, params_hash, coalesce(formed_at,'-infinity'))`; insert-only for `xd_app`.
  - `market.basket_member_scores(version_id fk, instrument_id fk, score double precision, rank int, primary key (version_id, instrument_id))`.
  - `market.basket_members.source_version_id` (from BK-01 `source_fetch_id` generalised: add a second nullable column `version_id` here if BK-01 shipped first).
  - `market.derived_definitions(basket_id pk/fk, definition_no int 1..14, enabled default false, cadence, params_set_id)` and `market.basket_param_sets(id, definition_no, params jsonb, params_hash, created_at, created_by)` (a param set is immutable; changing one makes a new row, selected by the owner).
- **Application:** `IDerivedUniverseReader` (bars as arrays for `as_of`, includes retired-with-bars), `IDerivedVersionStore`, `RefreshDerivedBasketsHandler` (command) calling the BK-02 evaluator; `GetBasketVersionsQuery`, `GetBasketScoresQuery`; ports in `Abstractions/`, handlers internal sealed, registered explicitly.
- **API** (session, tag `Baskets`):
  - `GET /api/baskets/{id}/versions` → `[{ versionId, asOf, effectiveFrom, formedAt, memberCount, universeSize, paramsHash, outcome }]` (paged).
  - `GET /api/baskets/{id}/versions/{versionId}` → members with `score`, `rank`, `excluded` list, `params`.
  - `POST /api/baskets/system/derived/refresh` (owner act, step-up) → run now; 409 `baskets.derived_disabled` when OFF.
  - `POST /api/baskets/system/derived/backfill` (owner act, step-up; body `{ basketId, from, to }`) → 202 with run id; bounded to 400 trading days.
  - `PUT /api/baskets/system/derived/{id}/params` (owner act) → creates a new param set, never edits one; validated by BK-02's ranges.
  - Problem codes: `baskets.system` 409, `baskets.derived_disabled` 409, `baskets.invalid` 400/422, `baskets.not_found` 404.
- **Settings** (`Baskets:System:Derived`, `[Range]`, `ValidateOnStart`): `Enabled` false; `DailyRunAfter` = after the EOD aggregation completes (event, not a clock); `IntradayFreezeTimes` 09:20 and 09:30 IST; `MaxBackfillDays` 400; `ScoresKeptBeyondMembers` 20; per-basket `enabled` in `derived_definitions`, all false.
- **Events / jobs:** service `derived-baskets` (`IScheduledService`, `After` the EOD aggregation, `CatchUpReach.Latest`), one run at a time (advisory lock + running row); intraday freezes triggered by the live bar close. Idempotent by `(basket, as_of, params_hash)`.

## Non-functional
Daily run for 14 baskets over 500 instruments under 20 s (BK-02 budget 2 s plus reading 260 daily bars x 500 in one query per basket group, shared across baskets); intraday freeze under 3 s after the bar closes. Log events `basket.derived` (basket, as_of, outcome, members, universe, excluded, ms; next free block at build); no instrument lists in logs. Storage: about 14 baskets x 250 versions/year x 120 score rows = 420k rows/year, trivial. Read-only to consumers; versions retained forever (they are the point-in-time record).

## Test plan
- **Unit:** next-session and previous-session calculation across weekend and holiday; version-window arithmetic (valid_to = next effective_from); hash stability.
- **Application (fakes):** idempotency; changed params gives a new version; previous ranks feed the buffer; not_formed when 1m bars are missing; disabled makes zero reads.
- **Integration (Testcontainers):** seed 120 instruments x 300 bars incl. a retired-after-D one; run; assert rows, `members(asOf)` on the boundary days (the day before, the effective day); transaction rollback leaves nothing; `xd_app` cannot update/delete `basket_versions`; backfill never overwrites `derived`; 409 routes; 401 without a session; contract snapshot.
- **Property:** appending a future bar does not change an earlier stored version when recomputed (backfill vs stored, apart from adjustment restatement flagged).
- **Architecture:** no order-named member; the job project references no HTTP client.

## Decisions (researched)
1. **Store every computed version; members are ordinary dated `basket_members` rows.** Reason: the dated model and `asOf` queries work unchanged, and a simulation reads what was published. Rejected: recompute on read (restated adjustments change old answers); a separate members table (second code path). Source: research §3.2.
2. **Effective the next trading session, `as_of` = last bar date.** Reason: strict no look-ahead; consumers take the version whose `effective_from <= decision time`. Rejected: same-day effective (uses bars not yet known at the decision). Source: research §2 header.
3. **Derived baskets are the only point-in-time membership we can offer, so backfill is allowed but labelled `recomputed`.** Reason: inputs are cut at `as_of`; adjustments restated retroactively are a stated limit. Rejected: no backfill (a year of waiting); overwriting published versions. Source: research §3.1, §3.4.
4. **Scores and ranks are stored beside members, plus 20 non-members.** Reason: UI "why" and buffer rule input. Rejected: members only (buffer would need a recompute). Source: research §3.2.
5. **Universe from instruments with a bar on `as_of`, retired included.** Reason: removes the retired-instrument survivorship hole; the residual bias is untracked instruments, hence the Nifty 500 tracking question. Rejected: today's tracked set. Source: research §3.4.
6. **Intraday baskets freeze once and are `not_formed` rather than guessed on missing data.** Source: research §2 notes 4, 12.
7. **Param sets immutable and hashed; owner chooses the active one.** Reason: avoid tuning on results and keep every version explainable; a change is an owner act recorded with the setting snapshot. Auto option: none needed, no approval is involved. Source: research §2 "Overfitting"; CLAUDE.md standing rules.
8. **Informational on change, no approval gate.** Source: research §3.3.

## Risks and honest limits
- Retroactively restated adjustments make backfilled versions differ slightly from what was knowable then.
- The universe is the owner's tracked set unless Nifty 500 is tracked.
- A job that writes membership the day before a scan uses it can change a scan scope: informational, shown in the version list.
- Version tables grow forever by design (small).

## Open questions
Track all Nifty 500 (BK-00 q3)? Which derived baskets to enable first? Is backfill wanted at all in the first release (it can be cut: -0.5 day)?

## UNVERIFIED
The name and event of the EOD aggregation completion (doc 05, not opened); the trading-calendar port name; live 1m bar-close hook for the intraday freeze (P3/P7 feed, not opened); `IScheduledService` `After` schedule semantics for a dependency on another job (only the list `[Daily, Weekly, After]` in `InstrumentMasterService.cs` was seen).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] ADR and doc 06 §3.1/§3.4 updated; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- **Intraday baskets are version-only:** Gappers, Opening-range wide and the intraday RVOL variant are stored only as `basket_versions` + `basket_member_scores` (with `formed_at`), **no `basket_members` rows**, because date-valued `valid_from` cannot represent same-day membership; `members(asOf)` for them returns the version formed that date (read from versions). They are not dated daily membership.
- **Same effective_from collision (param change):** the version with the newest `created_at` wins; the older version's member rows for that `effective_from` are closed with `valid_to = valid_from` (zero-length, check allows `>=`) and the older version stays readable.
- **Backfill:** only for a closed date range entirely before the first live (`derived`) version of that basket and with no overlap with open rows (`ux_basket_members_active`); otherwise 409 `baskets.backfill_overlap`. Range limited by the single setting `MaxBackfillDays` 400 (the API cites it, no second constant).
- **`failed`** is removed from `basket_versions.outcome` (values `applied|unchanged|not_formed`); failures live on the service run row.
- **Migration note:** the unique index uses `coalesce(formed_at,'-infinity')`, an expression index: written as raw SQL in the migration.
- **Retired instruments' bars (AC8):** requires `bars_agg` to keep retired instruments' bars; retention not verified. A blocking build check before Approved: confirm no retention/cleanup removes bars of retired instruments, else AC8 is dropped to "tracked as of D".
- **Blocking UNVERIFIED before Approved:** the EOD-aggregation completion event and the live 1m bar-close hook (doc 05, P3/P7 feed); BK-03 cannot be Approved until both are located.
- **PUT params** is an owner act with step-up and an audit entry holding the setting snapshot (old and new hash).
- **Universe uses BK-05 dated ban membership** (and ASM/GSM list) to fill the `Flags` of BK-02 inputs point-in-time.
- **Column name:** members reference the version through `source_ref` + `source_kind = 'version'` (BK-01); the earlier `version_id` is removed.
