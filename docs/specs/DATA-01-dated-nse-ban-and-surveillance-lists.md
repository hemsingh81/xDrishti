# DATA-01 — Dated NSE F&O-ban and ASM/GSM history, and the filters fed from it

- **Status:** Approved (coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended). Built 2026-11-12 (ADR 0274), not deployed.
- **Phase / roadmap row:** DATA — history, row DATA-01 (new; GAP-08)
- **Size:** about 3 agent-days (estimate; +0.5 d over the review's figure for the selection wiring found below) · **Needs:** BK-05 list baskets (ADR 0256), BK-01 fetch pattern (ADR 0254), CF-04 allow-list pattern (ADR 0195), SR-01 replay (ADR 0272)
- **Safety:** SAFETY (a network fetch of NSE archive files). OFF by default. The NSE terms reading, the ADR acceptance and the allow-list edit are the owner's acts. No order; no rule is loosened: an answer replaces `unavailable` only where a dated row exists, and a `fail` still rejects.
- **Design refs:** docs/design/10-trade-selection.md (filters), 19-risks-and-compliance.md; ADR 0195, 0254, 0256, 0272

## Goal
For any past session the system can answer "was this share in the F&O ban / under ASM or GSM on that date?" from stored, dated rows, so the live plan and the replay apply those filters from data, and the replay's `filters_without_history` label clears for the dates covered.

## Verified facts (main at 30777841)
- Dated list baskets exist **forward only**: `fo-ban` from NSE `fo_secban.csv`, dated by the trade date in line 1 (backend/src/XDrishti.Domain/Market/Baskets/Lists/ListBaskets.cs:32-33; BanListParser.cs:14-18; ADR 0256 §3), and `asm-gsm` from the Dhan master flags, "dated from the first recorded master refresh" (ListBaskets.cs:20-22). The fetch is `Baskets:System:Lists:FetchEnabled`, default false (BasketListOptions.cs:11,14). Nothing before the first fetch is stored.
- **The live plan never feeds these filters (beyond the review).** `SelectionPlanBuilder` builds `PlanCandidate` without `OnFnoBan` or `UnderSurveillance` (backend/src/XDrishti.Application/Planning/SelectionPlanBuilder.cs:75; only `ExcludedByEvent` is added, :84), so both filters always read `unavailable` (EligibilityFilters.cs:39-40) and **hold every candidate** unless the owner made them optional (EligibilityFilters.cs:44-48; SelectionRules.cs:17). The dated baskets are read today only by the derived-basket universe (DerivedUniverseReader.cs:41).
- The replay makes the event, F&O-ban and surveillance filters optional under `FiltersWithoutHistory` and labels the run `filters_without_history` (ReplaySessionRunner.cs:200-202; HindsightLabels.cs:25,52; ReplayParams.cs:14).
- The NSE host guard for basket files allows `nsearchives.nseindia.com` archive and `/content/fo/` paths (BasketSourceEndpointGuard.cs:19). Dated archive paths need an owner-added pattern, as for the daily history (ADR 0195 §2, `MasterDataEndpoints.Patterns`, shipped empty).
- **Correction to the review:** "ADR 0097" is not the allow-list ADR; it is the P7 phase check (ADR 0195:5 records the same slip). The terms gate is ADR 0254 §owner-acts 1 and ADR 0256 Status; the allow-list rule is ADR 0195 §2.
- The archive URL patterns for past `fo_secban_DDMMYYYY.csv` files and for NSE's ASM/GSM lists by date are **UNVERIFIED** (not probed; NSE publishes current ASM/GSM lists, a dated archive is not confirmed).

## Decisions (each as recommended)
1. **Store in the existing dated list baskets** (insert-only membership rows with the trade date), not a new table: one source for live, replay and the derived universe. *Recommended.*
2. **Probe first, one owner verb:** `xd data lists-probe --dates D1,D2` fetches at most 5 files, stores nothing, prints status and parse result; the backfill verb `xd data lists-backfill --from --to [--dry-run]` refuses until the patterns are on the allow-list and `Data:History:Lists:Enabled` (new, default false) is on.
3. **ASM/GSM history only if NSE has a dated archive.** If the probe finds none, ASM/GSM stays forward-only and the spec says so; no reconstruction from today's flags.
4. **Wire both filters from the dated baskets** in the live plan and the replay through one as-of port `IListFlagsAsOf.For(symbols, session)`: a dated row answers `true`/`false`; no row for that session answers `null` → `unavailable` (never "not banned"). Behind `Selection:ListFilters:Enabled`, default false, so the live plan does not change until the owner switches it.
5. **Replay label per date:** `filters_without_history` is kept for a run with any session not covered and the uncovered dates are listed; it is dropped only when every session of the run has a stored row for both lists.

## Scope
**In:** probe and backfill verbs; the as-of port; the two switches; wiring in `SelectionPlanBuilder` and the replay's fact context; label rule; tests; docs/design/10 and 17 lines; a new ADR (next free number at build).
**Out:** editing the allow-list (owner act); the event filter (P15); T2T list history; any change to filter thresholds.

## Acceptance criteria
1. With both switches off (default) no request is sent and the live plan's filter outcomes are unchanged (snapshot test of a stored plan input).
2. With the allow-list empty the backfill refuses with `history.not_allowed` before sending (test against the guard).
3. A fetched file is parsed fail-closed (`bad_header`, `bad_date`, `bad_row` as BanListParser); a rejected file stores nothing; a valid empty file stores an empty dated list (a covered day with no ban).
4. Rows are insert-only (a second load of the same date is a no-op, counted); no UPDATE or DELETE grant (grant test).
5. A session with no stored row for a list answers `unavailable`; a session with a row answers true/false (unit tests over the port).
6. With `Selection:ListFilters:Enabled` on, a banned share on its ban date is `Rejected` with `fno_ban`; the day after its ban ends it is not rejected by that filter.
7. A replay over 10 sessions where 7 are covered keeps `filters_without_history` and lists the 3 dates; over fully covered sessions the label is absent.
8. At most `MaxPerDay` requests per run with the CF-04 delay; no cookie, no redirect, GET only.

## Contract
- **CLI:** `xd data lists-probe --dates …`, `xd data lists-backfill --from … --to … [--dry-run]`. Exit codes as `xd data`.
- **Data:** no new table (membership rows of `fo-ban` / `asm-gsm`); a source column value `archive` to tell back-filled rows from daily ones (migration only if the column does not exist; checked at build).
- **Settings:** `Data:History:Lists:Enabled`, `Selection:ListFilters:Enabled` (both default false; compose variables per OPS-01).

## Test plan
Unit: parser, as-of port, label rule. Integration: guard refusal, insert-only, live-plan wiring on/off, replay label. Architecture: the new fetcher is the only caller of the archive pattern.

## Risks
NSE archive layout or bot blocking (UNVERIFIED user-agent acceptance, ADR 0254); turning the live wiring on changes which candidates are held — the owner should compare one nightly plan with it on in a dry run (`xd plan run --dry-run`) first.

## Done when
- [ ] ACs 1–8 have evidence; `scripts/check.sh` green; reviewer: no blockers
- [ ] ADR written; docs/design/10 and 17 updated; owner acts listed in the ADR

## As built (2026-11-12, ADR 0274)
- **Port and wiring:** `IListFlagsAsOf` (ban for the plan's date, ASM/GSM for the build session), `PlanListFilter`, `SelectionPlanBuilder` last optional parameter; the replay runner passes the same filter. `Selection:ListFilters:Enabled` and `Data:History:Lists:Enabled` default OFF.
- **Verbs:** `xd data lists-probe`, `xd data lists-backfill` (`DataListsCommand`). Ban list only: no dated ASM/GSM archive is known, so it stays forward-only.
- **Deviation:** two insert-only tables, `market.list_day_coverage` and `market.list_day_members` (migration `AddListDays`), because stays cannot record a covered empty day and are closed by UPDATE. A day is rejected if any banned symbol cannot be matched to the catalogue.
- **Label rule:** per session, both lists covered clears the label for that session (the event filter stays optional, noted); the run label clears only when no session carries it. The run-list row (no sessions read) still shows the parameter label.
- **Archive URL** `/archives/fo/sec_ban/fo_secban_DDMMYYYY.csv` is UNVERIFIED; the allow-list entry and the NSE terms are the owner's acts (ADR 0274).
- **Evidence:** ACs 1 to 8 by `ListFilterPlanTests`, `ListsBackfillTests`, `NseListArchiveSourceTests`, `ListDaysDatabaseTests`, `ListDaysGrantsTests`, `ReplayReviewTests`, `ListHistoryArchitectureTests`. `docs/design/10` and `17` lines not edited (the ADR carries the decision).
- **Review fixes (2026-11-12):** forward coverage is now exact (the daily ban fetch also writes a `daily` day row for its trade date; ASM/GSM is covered only on a date with a good refresh); the backfill stops at the first failed request (`Stopped=fetch_failed`); a run-level `event_filter_without_history` label; a literal pre-change hash test for the OFF plan. The daily day row is insert-only, so the first file stored for a date stands.
- **OPS-01 follow-up (open):** the compose variables for `Data__History__Lists__Enabled` and `Selection__ListFilters__Enabled` are not yet in `deploy/` (both default OFF in code); add them with the other OPS-01 switches.
