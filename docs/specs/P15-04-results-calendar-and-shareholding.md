# P15-04 — Results calendar and shareholding: board-meeting rows with their intimation time, shareholding rows, CSV loaders from the folder drop, `days_to_results`, and `unavailable` for the instrument with no entry

- **Status:** Built 2026-11-02 (ADR 0181). Approved (owner, 2026-11-02: pre-approved in the autonomous loop; open questions resolved with the recommended/safer answers listed in the spec)
- **Phase / roadmap row:** P15 — slice P15-04 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Calendar and shareholding"; "the calendar gives value early").
- **Design refs:** docs/design/20 §D · 16 (`exclude.results_within_days`) · 15 (pre-trade checks) · P15-00 risk 8 (look-ahead through the calendar), D3 · ADR 0033 (the date comes from the caller), 0087 (`unavailable` never passes). New ADR: **next free number at build time** (the calendar's own broadcast time, the event-session rule).
- **Needs:** P15-01 (the CSV drop, `row_level_ts`), P15-02 (`filings.filings`, `filings.calendar`, `filings.shareholding`, `IPointInTimeFilings`). **Needed by:** P15-05 (`ev-1`, `fu-1` features), P15-06 (the event filter and the checks).
- **Size:** 2 agent-days (estimate).
- **Safety:** no — local files and own tables only; it produces a number (`days_to_results`) but **no consumer on a decision path is added here** (P15-06 wires it, behind a default-off setting).

## Goal
The owner drops a CSV of board-meeting dates and a CSV of shareholding patterns; the system stores each row with the time the exchange made it public, and can answer, for any instrument and any instant, "how many trading sessions until its next results meeting, as known at that instant", or says `unavailable`. A meeting date that was announced only after the question's instant is never used for that instant.

## Verified facts (read 2026-11-02)
**Code:**
- `DaysToResults` is a field of `ChecklistFacts` (Domain/Checklist) and of the execution `CheckFacts`; both checklists return `unavailable` when it is null ("no results date for the instrument (no data source yet)"). A search of `backend/src` finds **no code that sets it**: no producer exists today. P15-00 says this slice supplies "`days_to_results` for the plan checks"; the wiring into the two checklists is not done here but in P15-06 (resolved below).
- `TradingCalendar.TradingDaysBetween(after, upTo)` counts trading days after `after` up to and including `upTo` (0 when not later); `ITradingCalendar.GetAsync` supplies the calendar.
- `filings.calendar` and `filings.shareholding` are created empty by P15-02 with the shapes fixed there.
**UNVERIFIED (exchange rules, not relied on for correctness):** that the intimation of a results board meeting is made several days before it (SEBI LODR Regulation 29), and that results follow the meeting on the same day. Nothing below assumes either: a missing intimation time is `unavailable`, and a meeting that the exchange never intimated simply has no row.

## Scope
**In:**
- Domain (pure): `CsvRows` parsers for the two formats, `ResultsDays` (the days rule), the calendar group/supersede resolution.
- Application: `CalendarLoader` and `ShareholdingLoader` (use cases that read `row_level_ts` entries from `IFilingsSource`), port `IResultsCalendar`, port `IShareholdingAsOf`.
- Extension of the `filings-ingest` run (P15-02) to call the two loaders; no new service.
- `GET /api/calendar/results`.
- Tests; ADR; doc 20 §D and doc 16 notes.

**Out:** parsing result filings (P15-03); the features that use these rows (P15-05); wiring `days_to_results` into the pre-trade checks, the simulator, plan selection or the loss tag (P15-06); a screen (P15-11); any fetch; any automatic guess of a meeting date (for example "last year's date plus 365 days") — no estimate is ever stored or returned.

## CSV formats (defined here)
UTF-8 (a BOM is accepted), LF or CRLF, comma separated, RFC 4180 quoting, a header row exactly as below (any other column name, order or count rejects the file `bad_header`), at most 20,000 rows and the P15-01 size limit. The drop sidecar names `kind` `board_meeting` or `shareholding`, the symbol field is `"*"` (a multi-company file), and `broadcast_ts` is absent (the rows carry it).

**Board meetings** — `symbol,period_end,meeting_date,purpose,status,intimation_ts`

| Column | Rule |
|---|---|
| `symbol` | NSE symbol of a tracked instrument |
| `period_end` | the quarter or year end the meeting is for, `YYYY-MM-DD` |
| `meeting_date` | `YYYY-MM-DD` |
| `purpose` | `results` or `other` (only `results` feeds `days_to_results`; `other` is stored) |
| `status` | `scheduled`, `rescheduled` or `cancelled` |
| `intimation_ts` | when the exchange published this row's information: ISO-8601 with an offset (same rule as P15-01); blank or invalid = `unavailable` |

**Shareholding** — `symbol,period_end,category,pct,pledged_pct,broadcast_ts`

| Column | Rule |
|---|---|
| `category` | `promoter`, `fii`, `dii`, `public` |
| `pct` | 0 to 100, up to 4 decimals; blank = `unavailable` for that row |
| `pledged_pct` | 0 to 100, the pledged share **of the promoter holding**, only on the `promoter` row; blank elsewhere or when not stated = `unavailable` |
| `broadcast_ts` | as above; all rows of one `(symbol, period_end)` must carry the same time (else the group is rejected `inconsistent_ts`) |

## Acceptance criteria
1. **Calendar rows keep their own time.** Given a CSV row for RELIANCE, period 2026-06-30, meeting 2026-08-12, `scheduled`, intimation 2026-08-03 11:00 +05:30, then one envelope (`kind board_meeting`, `broadcast_ts` = the intimation instant, `broadcast_state ok`) and one `filings.calendar` row are stored, in one transaction. The file's own hash is the base of the envelope hash (hash of the canonical row text), so the same row in two files is one row (idempotent), and re-loading a whole file inserts 0 rows.
2. **Look-ahead rule (the point of the slice).** Given the row of criterion 1, then `days_to_results(RELIANCE, t)` is `unavailable/no_entry` for every `t` before 2026-08-03 11:05 IST (lag 5), and from then on returns the days. A meeting that was **intimated after `t`** never contributes, even if its date lies before `t`'s next session. A rescheduling row (new meeting date, later intimation time) replaces the earlier one **only from its own visible time**: before it, the old date is still the answer.
3. **Supersede chain.** Rows of one `(instrument, period_end, purpose)` are ordered by intimation time; each later row supersedes the previous one (set at load from the stored current row). Equal times with different content are rejected `ambiguous_order`; a row older than the stored current row is stored with `supersedes` null and counted `out_of_order` (it is visible by its own time only and does not hide the newer one). A `cancelled` row makes the group's answer `unavailable/only_cancelled` from its own time until a later `scheduled`/`rescheduled` row.
4. **Days rule.** `ResultsDays` for instrument `i` and instant `t` (offset required): take the current visible `results` row per `period_end` that is not `cancelled`; the *event session* of a row is the first trading day on or after `meeting_date` (a Saturday meeting counts on the next Monday, a holiday on the next session); candidates are those whose event session is on or after the India date of `t`; choose the earliest; `days` = `TradingDaysBetween(date(t), eventSession)`. Hand cases (Friday 2026-08-07 is a session, 2026-08-08/09 are the weekend): meeting 2026-08-12 gives `days = 3` on Friday 08-07 (Mon, Tue, Wed after Friday), `1` on Tuesday 08-11, `0` on 08-12; a Saturday meeting 2026-08-08 has event session 2026-08-10 and gives `days = 1` on Friday 08-07. The date of `t` is the India date; a decision at 23:00 IST on 08-11 still counts 08-11 as today.
5. **No entry is unavailable, never zero.** Given an instrument with no visible `results` row, only `cancelled` rows, or only rows whose event session is already past, then the answer is `unavailable` with a closed reason `no_entry`, `only_cancelled` or `no_upcoming_entry`; a row with a null intimation time never counts (`unavailable_time` is counted in coverage). The word "far away" is never inferred from absence.
6. **Port.** `IResultsCalendar.DaysToResults(instrumentId, t, ct)` → `{ State: value | unavailable, Days?, MeetingDate?, EventSession?, PeriodEnd?, Reason? }` (also a batch form for a list of instruments, one query). It reads only through `IPointInTimeFilings`/the calendar table with the P15-02 visibility rule; a property test (as P15-02 criterion 8) shows that adding, changing or removing any calendar row with `intimation_ts + lag > t` leaves the answer at `t` identical, with a control that a visible row changes it.
7. **Shareholding rows.** Given a group (RELIANCE, 2026-06-30) with rows for the four categories and one time, then one envelope (`kind shareholding`, `period_end` set, the group's time) and four `filings.shareholding` rows are stored; a blank `pct` stores `state unavailable` and no number; `pct` outside 0-100 rejects the row `out_of_range`; a group whose `promoter + fii + dii + public` differs from 100 by more than 1.0 point is stored and flagged `sum_off` (counted, not corrected). A later group for the same key with a later time supersedes the earlier. `IShareholdingAsOf.Latest(instrumentId, t)` returns the current visible group (and the previous quarter's for a change), or `unavailable`.
8. **Rejections are counted, not silent.** Per file and per run: rows loaded, duplicate, rejected by reason (`bad_header`, `bad_row` with line number and column, `unknown_instrument`, `ambiguous_symbol`, `inconsistent_ts`, `out_of_range`, `ambiguous_order`), `unavailable` time, `out_of_order`, `sum_off`. A bad row never stops the good rows of the same file; a bad header rejects the whole file. Logs carry counts and codes only.
9. **API.** `GET /api/calendar/results?from&to&asOf` returns the *visible* upcoming results meetings with event session in the range: symbol, instrument id, `period_end`, `meeting_date`, `event_session`, `days` (from the India date of `asOf`, default now), `intimated_at`, `revision` (1 for the first row, 2 and up for reschedules), and `instrumentsWithoutEntry` (the count, and the first 300 symbols) among tracked instruments. 400 `calendar.invalid_query` for a range over 120 days, `from` after `to`, an `asOf` without an offset or an unknown parameter; an empty list (not an error) when nothing is stored. It returns no file content and no estimate.
10. **Same inputs, any order.** Loading the same files in any order, and the rows of a file in any order, gives identical stored ids and identical `days_to_results` answers (property test).
11. **No decision use** (architecture test): `IResultsCalendar` and `IShareholdingAsOf` are referenced only by their own use cases, the endpoint and tests; the checklists, `Simulation`, `Strategies*`, `Application/Planning|Learning|Selection` and `XDrishti.Execution` reference neither (P15-05/06 amend this test, naming the new caller). The no-order tripwire is green; with the loaders failing, every other service behaves as with them off.

## Contract
- **API:** `GET /api/calendar/results` (read-only; `ApiJsonContext`; `scripts/gen-api.sh`); problem `calendar.invalid_query`.
- **Data:** no new migration. Writers use `filings.filings`, `filings.calendar` and `filings.shareholding` of P15-02, insert-only for `xd_app` (the grants test of P15-02 already pins them). If the build finds a column missing, a small `AddCalendarColumns` migration with `Down` may add it; the tables stay insert-only.
- **Jobs:** the daily `filings-ingest` run (paused by default) calls both loaders after the file ingest; "Run now" stays allowed (local files only).
- **UI:** none in this slice (P15-11).

## Non-functional
`DaysToResults` ≤ 5 ms per instrument and ≤ 100 ms for a batch of 214 on the test container; loading a 4,300-row calendar CSV ≤ 5 s. No network. No estimate of any date is stored.

## Test plan
- **Unit (Domain):** both CSV parsers (header, quoting, BOM, CRLF, each bad row), the days rule on the hand cases of criterion 4 including a Saturday and a holiday meeting, the supersede and `cancelled` cases, `sum_off`, the property test of criterion 6 and the order-independence of criterion 10.
- **Application with fakes:** loaders (idempotent re-run, partial file, reschedule at a later time, out of order); `unavailable` reasons.
- **Integration (Timescale):** a synthetic drop folder end to end, the endpoint (each state, each 400, empty), concurrent loading.
- **Architecture:** criterion 11.

## Decisions (researched)
1. **Each calendar row carries its own intimation time, and a meeting date is "known" only from that time.** This is P15-00 risk 8: a date learned afterwards would let a backtest avoid a results day it could not have known. Rejected: one time per file; a retrospective calendar with no times (such a file loads as `unavailable` rows and changes nothing).
2. **The event is a session, counted in sessions.** `exclude.results_within_days` is read in sessions (P15-00 criterion 5: "the two sessions before and on the day"); a Saturday or holiday meeting lands on the next session, the conservative reading, because the reaction is then.
3. **Absence is unavailable, never "no results soon".** Matches ADR 0087 and the plan.
4. **No estimated dates.** A date inferred from last year would be a guess dressed as data and would break the look-ahead rule.
5. **`days_to_results` is a port here and is wired in P15-06.** Corrects the plan, which gave "for the plan checks" to this slice; the checks change behaviour and so belong behind P15-06's setting. No producer of `DaysToResults` exists in the code today.
6. **CSV rows, not the exchange's native formats.** The owner prepares one table per quarter from the exchange pages; the native formats are undocumented here (UNVERIFIED) and a parser for them would be guesswork.

## Risks and honest limits
- Only as complete as the owner's files: instruments with no row stay `unavailable` and are listed, never defaulted.
- The intimation time typed by hand is trusted (P15-01 risk). An unreliable time makes the look-ahead guard weaker, not absent: a blank time is unavailable.
- Results sometimes come out after the meeting day; the event session is therefore a lower bound for the broadcast, and the filter windows of P15-06 add margin.

## Open questions (owner), with the resolution used
1. CSV columns as defined? **Resolved: yes; a changed layout is a new version of this spec.**
2. Count days in sessions and a weekend meeting on the next session? **Resolved: yes.**
3. Show the instruments without an entry? **Resolved: yes, count plus the first 300 symbols.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any use of a meeting date before its intimation time, any default or estimate for a missing entry, and any caller outside the allowed list)
- [ ] Contract regenerated (`scripts/gen-api.sh`)
- [ ] ADR written; doc 20 §D (calendar, shareholding formats) and doc 16 (`exclude.results_within_days` read in sessions) updated
