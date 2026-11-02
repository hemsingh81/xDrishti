# P15-02 — Point-in-time filing store: insert-only tables, the supersedes chain, and `AsOf` with the broadcast-time rule

- **Status:** Approved (owner, 2026-11-02: pre-approved in the autonomous loop; open questions resolved with the recommended/safer answers listed in the spec)
- **Phase / roadmap row:** P15 — slice P15-02 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Point-in-time filing store").
- **Design refs:** docs/design/20 §D · 05 §10 · 19 §1 · P15-00 "Point-in-time rule (D3)" · ADR 0017, 0045 (insert-only for `xd_app`), 0066 (side-table pattern), 0173-0178 (recorder, disk guard, insert-only discipline). New ADR: **next free number at build time** (the point-in-time rule, the visibility definition, the unavailable states).
- **Needs:** P15-01 (the listing). **Needed by:** P15-03 (facts), P15-04 (calendar, shareholding), P15-05 (features), P15-06 (event filter).
- **Size:** 3 agent-days (estimate).
- **Safety:** no — it reads local files through the P15-01 port and writes its own tables; no network, no broker call, no setting that sends. It is the **first reader of filings on a path that later feeds decisions**, so the visibility rule is tested as a property, not by examples alone.

## Goal
Every filing the owner drops is stored once, immutably, with the exchange's broadcast time. A question "what did the market know about this company at instant `t`" has one answer, from `IPointInTimeFilings.AsOf`, and the answer cannot change when a filing published after `t` is added or revised. A filing without a broadcast time is stored, counted and never used by a backtest.

## Verified facts (read 2026-11-02)
**Code:**
- Insert-only tables for `xd_app` are made with `GrantTableToAppRole` then `MakeAppendOnlyForAppRole` per table (`MigrationBuilderExtensions`), never a schema-wide grant after it; `AddOptionArchiveQuality` is the latest example, with a sequence grant for an identity column. A new schema calls `GrantSchemaUsageToAppRole(schema)`; `Schemas.cs` holds the schema names (`Options = "options"` is the last of the P14 set).
- The runtime role is `xd_app`; `xd_exec` writes only `execution`; the P14 tables grant nothing to `xd_exec` or the read roles, and a grants test pins it per table. The latest migrations are `AddOptionContracts`, `AddOptionChainRecording`, `AddOptionBackfill`, `AddOptionArchiveQuality`.
- `TradingCalendar.TradingDaysBetween(after, upTo)` and `ITradingCalendar.GetAsync` exist; `DepthDiskGuard.Check` is pure and reused by every recorder. No `filings` schema, port or table exists.
**Plan facts:** the plan's table text says `broadcast_ts timestamptz not null` and also "`null` stored as `unavailable`"; these contradict. Resolved below: the column is **nullable**, null means unavailable.

## Scope
**In:**
- Domain (pure): `PointInTimeRule` (visibility, supersedes resolution), `AsOfResult`, `FilingId` (deterministic).
- Port `IPointInTimeFilings` (Application) and `IFilingStore` (insert, size read, coverage counts; reads rows only through `AsOf` and the metadata list).
- `FilingIngest` use case: list (P15-01) → resolve symbol → hash-check → copy to the store folder → insert; idempotent.
- Service `filings-ingest` in `xd-worker`, registered **paused**; the compose mount of `data/filings` into `xd-worker`.
- Migration `AddFilingsStore`: schema `filings`, five tables, insert-only.
- `GET /api/filings/coverage`, `GET /api/filings` (metadata only).
- Settings `Filings:LagMinutes`, `Filings:MaxFilesGb`, `Filings:MinFreeGb`; tests; ADR.

**Out:** parsing any file (P15-03 fills `result_facts`; P15-04 fills `calendar` and `shareholding`; P15-08 owns `filings.extractions`, which this migration does **not** create); features (P15-05); any use in selection or simulation (P15-04/06); deleting data; a screen (P15-11); the fetcher.

## The rule (D3) as built
- `lag` = `Filings:LagMinutes` (5, range 0-60), a **deployment** setting, not an owner-switchable one: it changes research results, so any record that reads `AsOf` stores the lag it used (P15-05).
- A row is **visible at `t`** iff `broadcast_ts` is not null and `broadcast_ts + lag ≤ t` (inclusive). Never `ingested_at`, never `period_end`, never a date.
- Rows form groups by `(instrument, kind, period_end)`; inside a group `supersedes` links a revision to the row it replaces. At `t`, a row is **current** iff it is visible and no visible row supersedes it. `AsOf` returns the current row of each group (the latest visible revision), so a restatement is invisible before its own `broadcast_ts + lag`.
- A superseding row must have `broadcast_ts` strictly later than the row it supersedes and the same instrument, kind and period; otherwise it is rejected `bad_supersede` and counted, not stored.
- Hand example: results for period 2026-06-30 broadcast Friday 2026-07-24 16:12 IST (10:42 UTC) with lag 5 are visible from 16:17 IST: at 16:16:59 `AsOf` returns nothing for that period, at 16:17:00 it returns the row. The first tradable session after it is Monday 2026-07-27. A restatement broadcast 2026-08-30 10:00 IST supersedes it: at 2026-08-29 `AsOf` still returns the original; from 2026-08-30 10:05 IST it returns the restatement only.

## Acceptance criteria
1. **Tables and grants.** The migration creates schema `filings` (usage granted to `xd_app`) and tables `filings.filings`, `filings.result_facts`, `filings.shareholding`, `filings.announcements`, `filings.calendar`. For each table `xd_app` has SELECT and INSERT and **not** UPDATE, DELETE or TRUNCATE; `xd_exec` and the read-only roles have **nothing** (grants test per table, as `ExecGrantsTests` and the options grant tests do). `Down` drops the five tables and the schema. The four fact tables are created empty by this slice (their writers are P15-03 and P15-04); a test pins that nothing in this slice inserts into them.
2. **Envelope (`filings.filings`).** Columns: `id uuid pk` (deterministic: the first 16 bytes of SHA-256 over `instrument_id | kind | period_end or "" | content_sha256`, so the same inputs in any order give the same id), `instrument_id uuid not null` (FK `market.instruments`), `kind` (check in `results`, `shareholding`, `board_meeting`, `announcement`), `period_end date null`, `broadcast_ts timestamptz null`, `broadcast_state` (check in `ok`, `unavailable`), `unavailable_reason varchar(30) null` (required when state is `unavailable`), `ingested_at timestamptz not null`, `source` (check in `folder_drop`), `content_sha256 char(64) not null` **unique**, `file_bytes int8`, `file_name varchar(120)`, `source_ref varchar(200) null`, `supersedes uuid null` (FK to `filings.filings`, check `supersedes <> id`). Indexes `(instrument_id, kind, broadcast_ts)` and `(kind, broadcast_ts)`. Check: `broadcast_state = 'ok'` ⇔ `broadcast_ts IS NOT NULL`.
3. **Ingest.** Given a drop with a valid sidecar, then one run stores one envelope per accepted file: symbol → instrument by exact symbol match among tracked instruments (an unknown or ambiguous symbol is `rejected: unknown_instrument` / `ambiguous_symbol`, counted, never guessed); the file is copied to `data/filings/files/<sha[0..2]>/<sha>` (`0600`, directory `0700`) **before** the row is inserted, and the copy is re-hashed; a failed copy inserts nothing. The inbox is left unchanged. An entry marked `row_level_ts` (a `.csv` of board meetings or shareholding, P15-01) is **not** stored here: it is counted `deferred_to_loader` and left for the P15-04 loader, which writes one envelope per row with the row's own time.
4. **Idempotent.** The same file twice (same bytes, same or different name) = one row (`ON CONFLICT (content_sha256) DO NOTHING`), counted `duplicate`; the same bytes with a **different** sidecar (another symbol, kind, period or time) is not stored and is counted `sidecar_conflict` with both values in the log, because changing a broadcast time after the fact would be a silent rewrite. A re-run of a whole inbox inserts 0 rows and copies 0 files. Two runs in parallel (two hosts) insert each file once.
5. **Revisions.** A sidecar field is **not** how a revision is declared in schema 1; a revision is detected: a new file for the same `(instrument, kind, period_end)` with different bytes and a later broadcast time is stored with `supersedes` = the current row of the group at ingest. A new file with an earlier or equal broadcast time than the current row is stored with `supersedes` null and `broadcast_state` unchanged but is flagged `out_of_order` in the run counts (it is visible by its own time; it does not hide the later one). Ties (equal times, different bytes) are not stored: `ambiguous_order`.
6. **`AsOf`.** `IPointInTimeFilings.AsOf(instrumentId, kind, t, ct)` returns `AsOfResult { State, Filings, UnavailableCount }`: `State` is `value` (at least one current row), `none_visible` (rows exist or none, but none is visible at `t` and none has a null time), or `unavailable` (no current row **and** at least one row of that instrument and kind has a null broadcast time, a count shown). `Filings` holds one current row per period, ordered by `period_end` descending then id. The hand example above passes at each instant. `t` must carry an offset (`DateTimeOffset`).
7. **Missing broadcast time.** A filing whose state is `unavailable` is stored, appears in the coverage counts per kind and reason (`unavailable_reason`: `absent`, `date_only`, `no_offset`, `unparsable`, `before_2000`, `broadcast_in_future`), is **never** returned in `Filings`, and its date is never substituted. `filings.unavailable` is logged as a count per run.
8. **Property test (the guard).** For random sets of filings (kinds, periods, restatements, null times) and a random `t`: (a) changing, adding or removing any row whose `broadcast_ts + lag > t`, or any row with a null time, leaves `AsOf(…, t)` byte-identical (compare the serialized result); (b) **control:** changing a visible current row **does** change it, so the test cannot pass by returning nothing; (c) a restatement published after `t` is invisible at `t` and a restatement published before `t` hides its predecessor; (d) `AsOf` is monotone in the sense that a larger `t` never un-sees a row that no later visible revision replaced; (e) the result does not depend on insertion order (shuffled inserts) or on `ingested_at`. At least 200 generated cases per property, fixed seed in CI.
9. **Service.** `filings-ingest` is registered **paused**, daily at 17:05 India time on trading days plus "Run now" (allowed: it touches only local files and its own tables), retries 1, notify on failure, no catch-up. With the service paused nothing scans the folder. A run ends with one counts line; a crash mid-run leaves no half row (copy first, insert second, one transaction per file).
10. **Disk guard.** Before each file, `DepthDiskGuard.Check` with `Filings:MaxFilesGb` (5, range 1-100) on the size of `data/filings/files` and `Filings:MinFreeGb` (20, range 5-500) free on the volume: either limit stops the run `disk_budget` / `disk_free` with a warning at 80 %; it stays stopped until cleared; **the app never deletes a filing or a file.**
11. **Coverage API.** `GET /api/filings/coverage?from&to` returns counts only: per kind and per quarter-end, `ok`, `unavailable` by reason, `superseded`, `out_of_order`, and the number of tracked instruments with no filing of that kind in the range. `GET /api/filings?instrument&kind&asOf` returns envelope **metadata** (no file content, no path), at most 200 rows, newest broadcast first; `asOf` applies the rule above. 400 `filings.invalid_query` for an unknown kind or instrument, a range over 800 days, or an `asOf` without an offset; an empty list (not an error) when nothing is stored; 404 `filings.not_found` for an unknown filing id. A kind with no data answers `unavailable`, not zero.
12. **No decision use yet** (architecture test): `IPointInTimeFilings` and the store are referenced only by the ingest use case, the two endpoints and tests; Domain trading code, `Simulation`, `Strategies*`, `Indicators`, `Application/Planning|Learning|Portfolio|Execution` and `XDrishti.Execution` reference no `Filings` type in this slice (P15-04/05/06 amend this test deliberately, naming the new caller). The no-order tripwire is green; a failing ingest changes nothing in any other service.
13. **Logging and secrets.** Logs `filings.ingested|duplicate|sidecar_conflict|unavailable|rejected|disk_stop` carry counts, ids and reason codes only; no file content, no path outside the data folder, no `source_ref`.

## Contract
- **API:** `GET /api/filings/coverage`, `GET /api/filings` (read-only; `ApiJsonContext`; `scripts/gen-api.sh`); problem codes `filings.invalid_query`, `filings.not_found`.
- **Data (`AddFilingsStore`, insert-only):**
  - `filings.filings`: criterion 2.
  - `filings.result_facts(filing_id uuid fk, field_code varchar(40), value numeric null, unit varchar(12), basis varchar(16), state in (value, unavailable), pk (filing_id, field_code, basis))` — shape fixed here, written by P15-03.
  - `filings.shareholding(filing_id uuid fk, category varchar(30), pct numeric null, pledged_pct numeric null, state, pk (filing_id, category))` — written by P15-04.
  - `filings.announcements(filing_id uuid pk fk, category varchar(60) null, headline varchar(300) null)` — the text lives in the stored file; no LLM columns (P15-08 adds `filings.extractions`).
  - `filings.calendar(filing_id uuid pk fk, meeting_date date, purpose varchar(20), status varchar(12), period_end date)` — written by P15-04.
  - No hypertable (a few hundred thousand rows over five years), no retention policy.
- **Files:** `data/filings/files/` (git-ignored, `0700`); compose mounts `../data/filings:/data/filings` into `xd-worker` (inbox read, files read-write).
- **Settings:** `Filings:LagMinutes` 5, `Filings:MaxFilesGb` 5, `Filings:MinFreeGb` 20, `Filings:Ingest:Enabled` is **not** a setting (the paused service is the gate).

## Data volume
Results: 4,280 envelopes over five years plus shareholding (4,280), calendar (about 4,300), announcements (up to 160 k): about 175 k envelope rows ≈ 60 MB with indexes. Files: results 1 to 2 GB raw (UNVERIFIED), PDFs optional (P15-00: 4 GB); the 5 GB cap is `filings.max_files_gb` of the plan.

## Non-functional
`AsOf` ≤ 5 ms per call on the test container with 200 k rows (index `(instrument_id, kind, broadcast_ts)`); ingest of one file ≤ 1 s including the copy and re-hash; memory under 50 MB. No cookie, token or URL with credentials in any log (none exists in this slice).

## Test plan
- **Unit (Domain):** visibility at the boundary (t = broadcast + lag minus 1 s, equal, plus 1 s), supersede resolution, `bad_supersede`, `out_of_order`, `ambiguous_order`, deterministic ids, state words; the property test of criterion 8 with its control.
- **Application with fakes:** ingest outcomes (accepted, duplicate, sidecar conflict, unknown symbol, unavailable time, disk stop), idempotent re-run, paused service reads nothing.
- **Integration (Timescale):** migration up/down, constraints (unique hash, state check, self-supersede), per-table grants for `xd_app`/`xd_exec`/read roles, concurrent ingest of the same file, `AsOf` against stored rows, both endpoints (each state, each 400, empty, 404).
- **Architecture:** criteria 12 and 13; Domain has no I/O.

## Decisions (researched)
1. **Nullable `broadcast_ts` with an explicit state.** Storing an unavailable filing keeps the coverage hole visible; a not-null column would force a guess (D3). Rejected: dropping such files.
2. **Visibility is inclusive (`≤`) and uses the lag at read time.** Matches the plan's rule; the lag is stored with each research record by its reader.
3. **A revision is detected, not declared.** A sidecar flag could be forgotten or abused; the group key and the later broadcast time define it, and every doubtful case is counted, not stored.
4. **Same bytes, different sidecar is a conflict, not an update.** Insert-only forbids rewriting a broadcast time; the owner fixes the sidecar and drops a corrected file only if the bytes differ.
5. **Deterministic ids and no identity column** for the envelope, so replays and order cannot change an id (plan criterion 9); the fact tables key on the filing id.
6. **Lag is a deployment setting.** Resolves a plan wording (`filings.lag_minutes` as an app setting): an owner-switchable lag would silently change past research.
7. **`filings.extractions` waits for P15-08**; creating an empty LLM table now would imply a decision path that does not exist.

## Risks and honest limits
- A wrong broadcast time supplied by hand is invisible here (P15-01 risk); coverage shows only absence.
- Revision detection by group assumes one results filing per period at a time; a split consolidated and standalone pair would be two kinds of one period (P15-03 adds a basis, not a new envelope kind).
- The files folder is the only copy: `xd-backup` must include `data/filings/` (small), unlike the recorder tables.

## Open questions (owner), with the resolution used
1. Lag 5 minutes as a deployment setting? **Resolved: yes (plan default).**
2. Store envelopes for files with no usable time? **Resolved: yes, as `unavailable`, counted.**
3. Service on or off after deploy? **Resolved: registered paused; the owner switches it on.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any path where `ingested_at`, a period end or a file date decides visibility, any update or delete grant, and any caller of `AsOf` outside the allowed list)
- [ ] Migration `AddFilingsStore` with `Down`; contract regenerated (`scripts/gen-api.sh`); compose mount added
- [ ] ADR written; doc 20 §D, doc 05 (new tables) and `deploy/CLAUDE.md` updated

## As built (2026-11-02)
- ADR 0180 (Proposed). Deviations from the text above, all conservative: the service runs in the scheduler of `xd-api` (the only host with one; `xd-worker` has no scheduler) and the compose mount is on `xd-api`; the unavailable reason `absent` is the P15-01 word `missing`; `GET /api/filings/{id}` was added for the 404; `asOf` needs an instrument and a kind; the out-of-order count in the coverage is derived (extra unlinked roots of a period); an undated filing is counted in `UnavailableCount`, so criterion 8(a) holds for the listed filings and, for filings broadcast too late to be known, for the whole answer; a new setting `Filings:FilesPath` names the store folder.
