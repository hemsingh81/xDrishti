# P3-01e — Breadth of the owner's tracked shares, point-in-time, and the market gate's VIX and breadth

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P3 — row P3-01e (Later, "before P7"); feeds the `breadth` context feature (P3-01c) and the market gate of P7-06.
- **Design refs:** docs/design/09-learning-engine.md §14 (market gate) · docs/design/11-strategy-lab.md §2 (market context, "Not yet built") · docs/design/05-data-and-brokers.md §4, §9 · docs/design/16-configuration.md (`smart_rules.market_gate`) · docs/design/18-roadmap.md (P3-01e row, ISS-22) · ADR 0033 (decision 4, "Known risk")
- **Needs:** [P3-01d](P3-01d-context-data-job-index-and-vix.md) (the table `market.context_daily`, `ContextUpkeep`, `IContextFigures`); built: `market.bars_agg` 1D rows, `IDailySeries`, `ICorporateActionQueries`, the smart rules (P7-06) and `StoredPlanContextSource` (P7-09).
- **Size:** 2.5 agent-days (the roadmap says 2): pure computation 0.6 · reads and store 0.6 · upkeep, rebuild verb 0.5 · gate wiring and units 0.4 · coverage, docs, tests 0.4. The extra half day is the gate wiring, which the roadmap row does not list.
- **Safety:** no. Read-only over stored bars; writes three numbers a day to one table; no order, secret, network or UI write.

## Goal
Each context day carries the share of the owner's tracked shares that closed above their 20-day average, computed only from closes up to that day, with the number of shares that voted and the number left out and why. It is labelled for what it is, the breadth of the owner's own tracked set, not of the market. The market gate of the plan gets a real VIX and a real breadth, in the unit it expects.

## Scope
**In:** pure `BreadthCalculator`; columns on `market.context_daily` (migration `AddContextBreadth`); the breadth step of `ContextUpkeep`; the verb `scripts/context.sh rebuild-breadth`; breadth in the coverage endpoint; `StoredPlanContextSource` fills `PlanContext.Gate` from `IContextFigures`; docs (doc 11 "Not yet built", doc 05 §9).
**Out:** `sector_rs(n)` (unplanned until `RS_ROTATION` needs it; needs a sector map and a rank, its own slice); breadth of the whole market (needs constituents of NSE indices as of each date, ISS-22); the events figure of the gate (P15); advance/decline lines, new highs and lows; a UI.

## Acceptance criteria
1. **Hand value.** Given the fixture of the Test plan (nine shares on day D), when breadth is computed, then `breadth` = 50.0, `breadth_voters` = 6, `breadth_universe` = 9, `breadth_excluded` = 3 with the reasons `warmup` 1, `no_bar` 1, `action_in_window` 1, and `breadth_state` = `value`.
2. **Rule.** A share votes "above" when its close on D is strictly greater than the simple mean of its closes on the 20 sessions of the calendar that end on D (D included); equal is not above; a share votes only when it has a complete 1D bar on each of those 20 sessions.
3. **Point-in-time.** Given any seed, when every close after D is changed or removed, then the breadth, voters and exclusions of D are identical (truncation test, 40 seeds); a share without a bar on D is excluded and counted, never read from an earlier day.
4. **Thin sample is not a number.** Given fewer than `BreadthMinVoters` (50) voters, or voters under `BreadthMinShare` (0.5) of the shares that have a bar on D, then `breadth` is null and `breadth_state` = `insufficient` with the counts; the coverage names the dates.
5. **Written once.** Given a day with breadth already computed, when the job runs again, then the stored value does not change, even if bars were loaded since; only `rebuild-breadth --from --to` changes it, prints for each changed day the old and new value and the count, and logs one event with the totals.
6. **Back-fill.** Given 1,100 context rows without breadth, when the upkeep runs, then all are computed in one pass over each share's 1D rows (one read per share), in at most 3 s for 214 shares.
7. **Units for the gate.** Given the latest figures `Vix` 19.4 and `Breadth` 27.5 (percent), when the plan context is read for a session after them, then `PlanContext.Gate` = `MarketGateInput(AsOf, 19.4, 0.275, null)`; a test fails if the breadth reaches `SmartRulesOptions.BreadthMin` (a fraction, 0 to 1) as a percent. With no figures the Gate stays null (unavailable, as today).
8. **Honest labels.** The coverage and the plan note say "breadth of your N tracked shares (members as of today), not of the market"; N is the universe of the latest day; a plan with the gate unavailable still carries the flag text of P7-06.
9. **The context feature.** Given rows with breadth, then the `breadth` feature of a frame reads the previous session's value (percent, 0 to 100); a row with null breadth gives `NaN` for that date (unchanged P3-01c behaviour).

## Contract
- **Data.** Migration `AddContextBreadth` on `market.context_daily`: `breadth double precision null check (breadth between 0 and 100)`, `breadth_voters smallint null`, `breadth_universe smallint null`, `breadth_excluded smallint null`, `breadth_state text null check (breadth_state in ('value','insufficient'))`, `breadth_computed_at timestamptz null`. Grants: the table stays insert-only for `xd_app` except `GRANT UPDATE (breadth, breadth_voters, breadth_universe, breadth_excluded, breadth_state, breadth_computed_at)` on those six columns (a Postgres column privilege), added by a new `MigrationBuilder` extension `GrantColumnUpdateToAppRole`; `ContextGrantsTests` (P3-01d) is extended to assert the index columns still refuse UPDATE.
- **Domain** (`Domain.Market.Context`): `BreadthInput(ShareId, Closes by session)`, `BreadthCalculator.Compute(date, sessions, shares, actionsByShare, options)` → `BreadthResult(Value?, Voters, Universe, Excluded, ExcludedByReason, State)`; `BreadthOptions(SmaSessions 20, MinVoters 50, MinShare 0.5)`; pure, no I/O, no clock.
- **Application:** `IBreadthSource` (the tracked, not retired shares, `type = 'share'`, ETFs excluded; their `1D` rows with `complete`; the ex-dates of their corporate actions whatever the status via `ICorporateActionQueries.ListKeysAsync`); `IContextDayStore.SetBreadthAsync(date, result)` (update `WHERE breadth_state IS NULL`); `ComputeBreadthHandler` called by `ContextUpkeep` after the index step; `RebuildBreadthHandler` for the verb. `IContextFigures` gains `Breadth` (already in `ContextFigures`) and `LatestAsync(beforeDate)` for the plan.
- **Infrastructure:** `BreadthSource` (Npgsql, one query per share, streamed, no `adjusted` read); `StoredPlanContextSource` reads `IContextFigures` and sets `Gate`; the note `PlanContextNotes.MarketGate` is added.
- **API:** the `GET /api/data/context` view of P3-01d gains `breadth { latest, state, voters, universe, excluded, insufficientDays, label }`; `scripts/gen-api.sh`.
- **Settings:** `Data:Context:Breadth:SmaSessions` 20 (fixed by the definition; kept in options so tests can shrink it, validated 2 to 250) · `MinVoters` 50 · `MinShare` 0.5.
- **Verb:** `scripts/context.sh rebuild-breadth --from <date> --to <date>` (both required; at most 400 sessions; dry run by default, `--write` to store; exit codes as P3-01d).
- **Log events** (next free ids): breadth computed (days, insufficient days, ms); breadth rebuilt (days changed). No symbols with prices.

## Non-functional
One pass of 214 shares × 1,300 rows (about 280,000 rows, about 20 MB) for the back-fill, a rolling sum per share (O(rows)); the daily run reads 21 rows per share. No look-ahead by construction (the window ends on D). Security: nothing leaves the host. A bad row (non-positive close) excludes that share for the window and is counted under `bad_bar`.

## Test plan
- **Unit:** the hand fixture below; the boundary cases (close equal to the mean; exactly `MinVoters`; exactly `MinShare`); a window that crosses a holiday (sessions come from the calendar, not from calendar days); the Muhurat evening as a session; ETFs and retired shares never counted; `bad_bar`.
- **Property:** truncation (criterion 3, 40 seeds) with the negative control "the calculator reads D+1" failing on at least 10 seeds; permutation of shares gives the same result; adding a share that cannot vote changes only `universe` and `excluded`.
- **Integration (Testcontainers):** column grants (UPDATE of breadth allowed, of `index_close` refused), write-once, the back-fill over 1,100 rows, rebuild dry run and write, the endpoint, the plan context with figures and without.
- **Gate:** `MarketGate` with `Vix` 19.4 and breadth 0.275 is `Reduced` (vix at or above 18; breadth below 0.30) and with events unknown the gate would be `Unavailable` were nothing tripped; the test pins both so the unit cannot drift.
- **Hand fixture (SmaSessions 20, MinVoters 5, MinShare 0.5):** shares A, B, C close 1, 2, … 20 over the 20 sessions (mean 10.5, close 20: above); D, E close 20, 19, … 1 (close 1: below); F closes 10 on all 20 (mean 10, not above); G has 19 bars (`warmup`); H has no bar on D (`no_bar`); I closes 1 … 20 but has a split with an ex-date on session 12 (`action_in_window`). Universe 9, bars on D 8, voters 6 (75 %), above 3, breadth = 100 × 3 / 6 = 50.0.

## Decisions (researched)
**D1 — Universe.** The owner's tracked, not retired **shares** (ETFs out: an index ETF would vote with the index). The doc says "tracked stocks above their 20-day average" (doc 09 §14, ADR 0033 decision 4). Limit: the set is today's list, not the members of each past date (ISS-22), so early days describe the owner's present picks.
**D2 — Raw closes, with a vote left out when an action falls in the window.** Rejected: adjusting closes by the applied factors. The owner's CSV prices are already adjusted at source (the double-adjustment guard of P3-04a exists for that reason), so adjusting them again would manufacture a jump; adjusting only the Dhan-sourced ones needs per-bar provenance. Leaving out a share whose window holds an ex-date (any status, `ICorporateActionQueries.ListKeysAsync`) costs a handful of votes for 20 sessions and cannot be wrong in either data case. The status `ignored` is included on purpose (conservative).
**D3 — Strict window.** A share votes only with all 20 sessions complete. Alternative rejected: tolerating gaps (a halt would shift the mean silently). The cost is reported as `excluded` and `warmup` counts, not hidden.
**D4 — Written once.** Breadth is computed when first needed and kept, for the same reason the index rows are insert-only (P3-01d D10): a later backfill of stock bars must not rewrite what a plan saw. Rebuild is explicit, previewed, and logged.
**D5 — Threshold for a number.** At least 50 voters and half of the shares that have a bar: 50 voters keeps the standard error of a percentage under about 7 points (sqrt(.25/50)), below that the figure says `insufficient`. A different number is a setting. Source of the arithmetic: the binomial standard error, no external source needed.
**D6 — Units.** `ContextDay.Breadth` and the feature are percent (0 to 100, ADR 0033); `SmartRulesOptions.BreadthMin` and doc 16 `breadth_min: 0.30` are fractions. The wiring divides by 100 once, at `StoredPlanContextSource`, with a test; no other layer converts.
**D7 — The gate stays honest about events.** `MarketGate` returns `Normal` only when VIX, breadth and the events figure are all known; the events calendar does not exist before P15, so with this slice the gate can be `Reduced` or `NoTrade` on a real VIX or breadth, and otherwise stays `Unavailable` with the three-figure list. Rejected: passing `HighImpactEventToday = false` (it would claim a calendar the system does not have). Finding for the index.
**D8 — `sector_rs(n)`.** Not built (the roadmap's decision stands): no strategy of the 15 stored ones reads it (`RS_ROTATION` is out of P6-17b). When one does, it needs a sector map per share (the master has `industry`), a daily return per sector and a rank, about 2 agent-days; its own spec then.
**D9 — Label.** "breadth of your N tracked shares" everywhere a number is shown; never "market breadth".

## Risks and honest limits
Early days rest on fewer shares and on today's picks (survivorship, ISS-22); breadth moves with the owner's list, so adding 50 shares changes later days but not stored ones, which makes the series inhomogeneous: `breadth_universe` is stored beside every value so a reader can see it. A 20-day mean is slow; one big day moves a thin set. A gate built on it is a rule of thumb from doc 09, with no evidence yet that it helps (P7 will measure). The CSV history ends 2026-04-30, so breadth after it needs the Dhan fetch.

## Open questions
None. (Owner acts that change the data: tracking more shares; running the Dhan phase.)

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers or majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddContextBreadth` added
- [ ] doc 11 "Not yet built", doc 05 §9 and the roadmap row updated; `scripts/build-docs.sh`
