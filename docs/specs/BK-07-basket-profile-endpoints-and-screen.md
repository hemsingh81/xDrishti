# BK-07 — Basket profile endpoints and screen

- **Status:** Draft (written 2026-10-25; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P8 follow-up — basket performance insight (research note `docs/research/basket-leverage-uses.md` §2, views 1 to 7)
- **Design refs:** docs/design/13-reports-and-ui.md §1c · docs/design/06-instruments-and-baskets.md §3, §4 · docs/prototype/ (baskets and reports views) · ADR 0100, 0102, 0104, 0105 · ISS-22
- **Needs:** [BK-06](BK-06-basket-index-and-profile-metrics-engine-pure.md) (the kernels); P8-06 (built: `MetricView`, `Evidence`, run default set, 422/404 problem codes); P8-07 (built: benchmarks); P8-09 (built: comparison, pivot, `HeatGrid` in `shared/analytics`, `lensToQuery`); the members-as-of query (`members?asOf`, BK-01), `IBarQueries.ReadDailyBarsAsync`, `IPortfolioBenchmark` (existing ports). Soft: BK-01..BK-05 (system baskets with dated membership and `basket_versions`/`params_hash`; without them the screen works on manual baskets with `single_snapshot` membership), BK-08 (cluster ids for the correlation view; without them the matrix is sector-ordered), `market.context_daily` regimes (P3-01c/d).
- **Size:** 7 agent-days. Suggested split if it overruns: **BK-07a backend 3 d** (handler, endpoints, DTOs, cache, hold-out and overlap kernels, tests), **BK-07b screen 4 d** (route and panels).
- **Safety:** no. GET only; no write, no job, no setting; the read-only folder test covers the new feature folder.

## Goal
From a basket (any kind) the owner opens one lazy screen that shows its full performance insight: rebased line against the index with the difference area, risk table, drawdown, rolling beta, relative strength and its rank among baskets, breadth, dispersion and correlation inside the basket, contributions, regime panel, turnover and capacity, and the strategy-by-basket evidence already in Reports — every figure with its n or its honest non-value state, and the survivorship and membership basis printed.

## Scope
**In:** `GET /api/reports/baskets/{id}/profile`, `GET /api/reports/baskets/ranking`; an Application handler composing membership, daily bars, the benchmark, regimes and ADV into the BK-06 input; DTOs; a result cache; the two small strategy-family kernels `HoldOutStability` (m23) and `MemberOverlap` (Jaccard, m22) in `Domain/Reporting`; lazy route `/baskets/:basketId/profile` and `/baskets/ranking`; feature folder `features/basket-profile`; fixtures and tests; ADR.
**Out:** any metric formula (BK-06); storing breadth or correlation per day (BK-08); clustering (BK-08); strategy tables that P8-09 already shows (the screen links to Reports with the basket preselected, it does not repeat them); m20 basket × regime × strategy (needs regime tags on trades, P6-02: shown as a disabled panel with the reason); m21 sim-versus-paper (P10); exports; saving a view; a rupee figure; a "best basket" label.

## Acceptance criteria
1. **Endpoint and window.** Given `GET /api/reports/baskets/{id}/profile?from&to&index&membersAsOf`, then it returns `BasketProfileView` for a manual or system basket; `from`/`to` default to the last 252 sessions ending at the newest stored daily bar (said in `window.source`); `index` defaults to the owner's benchmark (`IPortfolioBenchmark`); unknown or archived id is 404 `reports.not_found`; `from` not before `to`, a window under 20 sessions, a malformed id or more than 300 members' worth of reads beyond `MaxInstruments` is 422 `reports.invalid`. A basket of 300 or fewer reads at most one daily-bar query per member plus one for the index.
2. **All metrics present.** Given the seeded fixture (3 members, hand-worked in the Test plan), then the view contains m1b to m16 exactly as BK-06 states them; each is a `MetricView {state, number, reason, n, min}`; none is a bare number.
3. **Honest non-value states.** Given a basket with 4 members, then breadth, dispersion and correlation are `insufficient` (n 4, min 5) and the screen shows the reason text, no number, no chart line, and no colour; given a basket with no bars, `empty`; given no benchmark bars, the index-relative metrics are `unavailable` with where the owner can track it (text only); given no regime codes, the regime panel is `unavailable` with the reason. A test renders every state of every metric and fails if a digit appears for a non-value (shared helper with P8-08/09).
4. **Membership basis printed.** Given a basket with dated membership, then `membershipBasis` is `dated` with the first date; with one snapshot, `single_snapshot` and the line "members as of <date>, not as of each trading day; survivorship applies (ISS-22)"; the line is on the header of the screen, not in a tooltip.
5. **Exclusions named.** Given excluded instruments or instrument-days, then the header shows "N of M instruments counted" and an expandable list with symbol and reason (`no_bars|starts_late|ends_early|discontinuity|bad_price|short_history`), as P8-09 does for the hold column.
6. **Ranking board.** Given `GET /api/reports/baskets/ranking?basket=<2 to 12 ids>&from&to`, then each basket has RS rank (m8, 1 = strongest) and risk-adjusted return (return divided by annualised vol); a basket with an `insufficient` input shows the state and **no rank**; given ranks whose gap is within the noise rule (see Decision 3) the board prints "not separated" instead of an order; no label "best".
7. **Overlap and hold-out.** Given two baskets sharing members, then `MemberOverlap` returns the Jaccard index of their member sets as of the date, by hand A = {1,2,3,4}, B = {3,4,5} gives 2/5 = 0.4000; the ranking board prints the note "baskets overlap; results are not independent". Given the strategy trades of a basket in the default run set, then `HoldOutStability` splits at the median exit date and returns, per (strategy, basket) cell with ≥ 30 trades in each half, the sign of expectancy in each half and "agrees" or "disagrees"; a cell under 30 in either half is `insufficient` (n per half).
8. **Strategy panel, not a copy.** Given the strategy section, then it shows the P8-09 comparison row for this basket (read from the built endpoints `groups?lens=basket`, `what-works?lens=basket,strategy`), the hold-out result, and a link "Open in Reports" that carries `basket=<id>`; the basket × regime panel is shown disabled with the P8-09 server reason.
9. **Caching.** Given the same (basket id, `params_hash`, membership date, window, index, newest bar date) key twice, then the second call reads no bars (a counting fake proves it) and returns identical bytes; a new stored bar changes the key.
10. **Performance.** p95 under 1.5 s for 6 baskets × 50 members × 300 bars on the seeded fixture (the profile of one basket under 600 ms; the ranking call computes the m1b series only, not the full profile); the number of bar reads is logged.
11. **Evidence block.** Every response carries `Evidence {Basis "equal weight, daily rebalanced, price return, dividends not included", N, Range, Limits, UnavailableInputs}`; a test fails if one lacks it. The same stored data gives identical bytes.
12. **Screen states.** Loading, empty (no members), error (with retry) and each panel's own non-value state; the screen at 360 px scrolls tables inside their cards with the first column fixed, charts at least 220 px tall, touch targets 44 px, no horizontal page scroll; every chart has a table with the same numbers; status never by colour alone.
13. **Lazy and light.** The screen is a lazy route; the initial bundle gains nothing beyond the route definition (`npm run size` unchanged within 1 KB); no chart library is added (reuse `TimeSeriesChart`, `HeatGrid`, `BarList`).
14. **Read-only.** The folder test proves `features/basket-profile` issues only GET; no markup is rendered from basket or instrument names (text only).

## Contract
- **API (tag `Reports`):**
  - `GET /api/reports/baskets/{id}/profile` (name `GetBasketProfile`) params `from`, `to`, `index` (instrument id), `membersAsOf`. `BasketProfileView { Basket {Id, Name, Kind, ParamsHash?}, Window {From, To, Source}, MembersAsOf, MembershipBasis, MembershipFrom?, Members {Total, Counted, Excluded[{Symbol, Reason}]}, Index {Symbol, IsDefault}, Hold: MetricView (from P8-07), Series {Dates[], Basket[], Index[], RelativeStrength[], Drawdown[], RollingBeta60[]}, Risk {Vol20, Vol60, Vol252, Mdd, MddDuration, Beta60, Beta252, Alpha, TrackingError, InformationRatio, UpCapture, DownCapture}, IndexDifference {Basket, Index, Difference}, Breadth {B20, B50, B200, AdShare, NewHighShare, SeriesB20[]}, Dispersion {Today, Mean20, Iqr}, Correlation {RhoBar, NEff, Sampled, Matrix? (N ≤ 40)}, Contribution {Top[], Bottom[], Top3Share}, Regime[{Code, Label, Days, MeanReturn, Vol, HitRate, State}], Turnover, Capacity, Evidence }`.
  - `GET /api/reports/baskets/ranking` (name `GetBasketRanking`) params `basket` (2 to 12 ids), `from`, `to`, `index`. `BasketRankingView { Rows[{BasketId, Name, RsReturn21/63/126/252, RsRank, RiskAdjusted, State}], Overlap[{A, B, Jaccard}], Separated, Evidence }`.
  - Problem codes: `reports.invalid` 422, `reports.not_found` 404. Both endpoints need a session.
- **Application:** `GetBasketProfileQuery/Handler`, `GetBasketRankingQuery/Handler` (internal sealed, registered in `DependencyInjection.cs`); one in-memory cache behind an interface (`IProfileCache`, bounded, keyed as criterion 9). **Domain:** `HoldOutStability`, `MemberOverlap` (pure). No new table, port or migration.
- **Settings** (`Reports:BasketProfile`, `[Range]`, `ValidateOnStart`): `MinVoters` 5, `MinDaysPerRegime` 30, `CapacityPct` 0.01, `MaxInstruments` 300, `MaxBaskets` 12, `DefaultWindowSessions` 252 (the discontinuity threshold is the P8-07 `Reports:Benchmark` value, one definition).
- **UI:** feature `basket-profile`: `BasketProfilePage`, `ProfileHeader` (window, index, basis, exclusions), `RebasedChart` (basket against index, difference area, `TimeSeriesChart`), `RiskTable`, `DrawdownRibbon`, `RollingBetaChart`, `RelativeStrengthPanel`, `InsidePanel` (breadth lines, dispersion, rho_bar), `CorrelationPanel` (`HeatGrid` for N ≤ 40, otherwise "too many members for a matrix: showing the 40 most liquid"; cluster ids once BK-08 exists), `ContributionBars` (`BarList`), `MemberTable` (virtualised above 100 rows), `RegimePanel`, `TurnoverCapacityPanel`, `StrategyPanel`, `RankingPage`, `RankingBoard`. Each in its own file, ≤ 250 lines; URL state `from`, `to`, `index`, `asOf`, `tab`. Entry points: a "Performance" link on each basket in the Baskets screen and on a P8-09 comparison row; prototype palette and the P8 kit (`MetricValue`, `NonValue`, `EvidenceBlock`).
- **Events/jobs:** none. Log event `report.basket_profile` (basket, members, reads, cacheHit, ms; next free id block at build time), counts only.

## Non-functional
See criterion 10. Memory: the 300 × 300 close arrays are transient. Security: read only, session protected, no names in logs.

## Test plan
- **Application (fakes):** the seeded fixture: three members and an index over five sessions, a membership change on session 3, one member with a −50 % one-day jump, one with no bars; assert the handler passes BK-06 the right series and weights from the previous close; an archived basket 404; the cache key; one bar read per member.
- **Integration (Testcontainers):** seed bars for the three members and the index, one dated membership change, regime codes for the window; assert the response and the evidence block; 401 without a session; contract snapshot.
- **Screen tests (MSW, `pinClock`):** every non-value state per panel; the 4-member basket; dated and single-snapshot basis lines; excluded list; ranking with an insufficient basket and with "not separated"; the disabled regime × strategy panel; keyboard path through the correlation grid; 360 px layout checks as P8-09; bundle and read-only tests.
- **Properties:** a number if and only if the state is value, over the whole view; adding an excluded instrument never changes a figure.
- **Architecture:** no order-named member; no new Infrastructure type.

## Decisions (researched)
1. **One profile endpoint per basket, one ranking endpoint across baskets.** Question: many small reports or one? Chosen: one profile (the panels share the same series, so one pass over the bars) and a lighter ranking call that computes only m1b and m8. Reason: the profile's cost is the bar reads, shared by every panel; separate endpoints would read the bars again. Rejected: a catalogue entry per metric (P8-10 style), 15 round trips for one screen.
2. **A new lazy route, not a tab inside Reports.** Reports is built around trades and runs (one `ReportQuery`); a profile is about a basket's own prices and has no run set. Folding it in would force fake run filters. The screen links to Reports for the strategy evidence instead of copying it (P8-00 D4: one query model).
3. **No order without separation.** Chosen: ranks are shown only when every ranked basket has a value; the board says "not separated" when the RS-return gaps between neighbours are smaller than the standard error of the difference, approximated as the tracking error of the pair × sqrt(window days / 252). Reason: P8-00 D5 and research §4 item 5 (no ranking by small samples). Rejected: always sorting by raw return (rewards a lucky window). The noise rule is a stored fact the screen prints; it is not a significance test and says so.
4. **Hold-out split at the median exit date, sign agreement only.** Chosen: m23 compares the sign of expectancy in the two halves, needing 30 trades in each. Reason: the cheapest guard against reading one lucky half (research m23); a stability score would invite over-reading. Rejected: rolling-window stability (needs more trades than a basket has).
5. **Jaccard of member sets as of one date.** Chosen: it is the honest size of the overlap that P8-09 only mentions in words. Rejected: overlap by trade (already P8-09's non-additive note).
6. **Profile reads membership as of a stated date, with dated history when BK-01..05 provide it.** Same rule as P8-07 Decision 2, extended: when dated rows exist the daily index uses them (BK-06 Decision 2); when not, the single snapshot and the survivorship line.
7. **Cache in process, not in the database.** The inputs are stored bars; a stale key is impossible because the newest bar date is in the key. Rejected: a table of precomputed profiles (a second source of truth that goes stale with each restated bar).
8. **Correlation matrix only up to 40 members.** Beyond that a matrix is unreadable and 1,600 cells exceed what a phone can show; the view shows the 40 most liquid and says so. Rejected: scrolling 200 × 200 grids.

## Risks and honest limits
- Survivorship and the single-snapshot problem (criterion 4); a basket assembled from winners shows an inflated line.
- Thin histories: with two to three years of data most 252-day figures are `insufficient` early on; that is the intended result.
- The profile describes the past; no number here predicts. The screen carries no "buy", "sell" or "increase" wording.
- Layout unverified in a real browser until P8-16's checklist is extended to this screen.

## UNVERIFIED
- The shape of BK-01..BK-05 outputs (dated rows, `basket_versions`/`params_hash`, kinds beyond `manual`/`portfolio`: `BasketKinds` today has only those two, VERIFIED in code).
- Whether `IBarQueries.ReadDailyBarsAsync` returns volume (needed for ADV20); if not, capacity is `unavailable` until a port method is added (one read, no table).
- Regime availability in `market.context_daily` for the window.
- That `HeatGrid` handles a 40 × 40 grid within the 250-line-per-component rule without a virtual layer (a measured check at build).

## Alignment with BK-01..BK-05 (read 2026-10-25)
BK-00..BK-05 now exist (Draft). What this slice relies on, as they state it: `kind` in `manual|portfolio|index|list|derived` and `system` (BK-01); `GET /api/baskets/{id}/members?asOf=` with `membersKnownSince` (BK-01); derived baskets stored as immutable dated versions in `market.basket_versions` (`as_of`, `effective_from`, `params_hash`, member rows with `valid_from`/`valid_to`) written by a job that is OFF by default (BK-03); list baskets for F&O, ASM/GSM and MTF (BK-05). Wherever this spec says `rule_version` it means the basket's version id plus `params_hash` (null for manual and index baskets, whose history starts at the first fetch). Index baskets are point in time only from the first fetch; the screens say so. Re-check names and ids at Approval; nothing here edits BK-00..BK-05.

## Open questions
1. Is the route name `/baskets/:basketId/profile` acceptable, or should the profile live under Reports (`/reports/baskets/:id`)?
2. Which index is the default for broad baskets (see BK-06 Q1)?

## Done when
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green; reviewer: no blockers/majors
- [ ] `scripts/gen-api.sh` run; no migration
- [ ] ADR (next free number at build time), roadmap row, doc 13 §1c updated, `scripts/build-docs.sh`

## Review fixes 2026-10-25
- **P8-07 Decision 5 conflict resolved:** the "difference to the index" figure (field `IndexDifference`, chart label "difference area") is rendered only on this screen's passive-return card and never in the same card or view as any trading result (the P8-09 strategy panel, hold-out, comparison row). The words "excess", "alpha" and "outperformance" never label it (alpha appears only as the regression intercept m6, named "intercept vs index"). New test: a render test fails if the difference figure and a trading-result metric share a card or view, or if those three words appear in the profile screen text for that figure.
- The ranking board prints "indicative, pairwise, uncorrected" beside the "not separated" rule (it compares neighbouring pairs, with no multiple-comparison correction).
- `rule version`/`rule_version`/`RuleVersion`/`IBasketQueries` replaced in the body by `params_hash`, `basket_versions`, `members?asOf`.
- Size unchanged: 7 agent-days (+0.2 for the new test, absorbed).
