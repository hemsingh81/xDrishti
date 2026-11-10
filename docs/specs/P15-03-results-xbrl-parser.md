# P15-03 — Results XBRL parser: a pure, hardened parser for stored results filings, facts per span and basis, `unavailable` for every field the file does not carry

- **Status:** Built 2026-11-02 (ADR 0184). Approved (owner, 2026-11-02, "consider all is approved and continue in loop")
- **Phase / roadmap row:** P15 — slice P15-03 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Results XBRL parser").
- **Design refs:** docs/design/20 §D · 05 §10 · 19 §1 · P15-00 D3 (point in time), D4 (XBRL is the source of truth for numbers) · ADR 0179 (untrusted files), 0180 (the store, the supersede chain, "consolidated and standalone are for P15-03 to separate"), 0066 (side-table pattern). New ADR: **next free number at build time** (the fact shape, the span and basis rules, the parser version).
- **Needs:** P15-02 (built: `filings.filings`, `filings.result_facts`, `IPointInTimeFilings`, the `filings-ingest` run). **Owner act:** five real results filings (XBRL plus the PDF of the same filing, for the hand values), at least one consolidated and one standalone, one annual (audited) and, optionally, one bank. **Needed by:** P15-05 (`fu-1`), P15-06's loss tag already reads envelope times only (unchanged).
- **Size:** 3 agent-days (estimate; the plan said 3; the reshape of the empty fact table and the per-basis read are inside it).
- **Safety:** no — local files and its own tables, no network, no decision reader. The parser reads **untrusted XML**, so it is built as a security-sensitive parser (no DTD, no entity, no external resolution, bounded size and depth), and every doubtful number is `unavailable`, never a guess.

## Goal
For every stored results filing in XBRL, the system holds the reported revenue, total income, profit before tax, profit after tax, basic and diluted EPS and exceptional items, per span (quarter, year to date, full year) and per basis (consolidated, standalone), with the audited flag, in rupees, as filed. A field the file does not carry, or carries ambiguously, is `unavailable`. A reader at instant `t` sees only facts of filings visible at `t`.

## Verified facts (read 2026-11-02)
**Code:**
- `filings.result_facts` exists (migration `AddFilingsStore`) with `filing_id, field_code varchar(40), value numeric null, unit varchar(12), basis varchar(16), state in (value, unavailable)`, PK `(filing_id, field_code, basis)`, insert-only for `xd_app`, nothing for `xd_exec` or the read roles. **No code writes it** (a P15-02 architecture test pins that nothing inserts into it). It has no column for the span, the audited flag, the parser version or an unavailable reason.
- P15-02 groups envelopes by `(instrument, kind, period_end)` and links a later file of a group as a **revision** (`supersedes`). ADR 0180 known limit: consolidated and standalone files of one period would therefore be chained as if one restated the other.
- `IPointInTimeFilings.AsOf` returns the **current** envelope per period only. Accepted extensions are `.xml`, `.xbrl`, `.csv`, `.pdf`, `.txt`; `Filings:MaxFileMb` is 20. No XML parsing code exists anywhere in `backend/src` (no `XmlReader` use).
**UNVERIFIED (decided by the fixtures, not by this text):** the NSE/BSE taxonomy element names (the mapping table below is a starting list), whether values are in rupees or scaled (a rounding-level element), how the basis is marked (an element such as `NatureOfReportStandaloneConsolidated` or a context dimension), and the banking format's elements. Historical XBRL before about 2018 is UNVERIFIED (P15-00 risk 3).

## Scope
**In:**
- Domain (pure): `ResultsXbrlParser` (bytes → `ParsedResults`), `ResultsTaxonomyMap` (versioned element-to-field table, `xbrl-1`), `SpanRule`, `FactSanity` (flags only).
- Hardened reader settings (Infrastructure helper used by the parser's caller; the Domain parser works on a pre-tokenised stream or on `XmlReader` created only by `SafeXml.Create`).
- Application: `ParseResultsHandler` (one use case) called in the `filings-ingest` run after the file ingest and the P15-04 loaders; port `IResultFactsAsOf`.
- Migration `ReshapeResultFacts` and table `filings.result_parses`.
- `GET /api/filings/{id}/facts` (read-only).
- Tests (synthetic and real fixtures), ADR, doc 20 §D.

**Out:** PDF results (stored, never parsed: `not_xbrl`); any number from an LLM (D4); features (P15-05); a correction of a reported number (sanity flags only); analyst estimates; a screen (P15-11); BSE-specific formats.

## The rules
- **Fields** (`field_code`): `revenue` (revenue from operations), `total_income`, `pbt`, `pat` (profit for the period attributable to owners when stated, else profit for the period, recorded in the reason), `eps_basic`, `eps_diluted`, `exceptional_items`. Units: `inr` (rupees), `inr_per_share`.
- **Span** from the XBRL context period: a duration of 80 to 100 days ending on `period_end` is `q`; a duration starting on the financial-year start (1 April) and longer than 100 days is `ytd`, or `fy` when it is 355 to 375 days; any other duration, an instant context for a flow item, or a period not ending on the envelope's `period_end` is skipped and counted `unknown_span`.
- **Basis** `consolidated` or `standalone` from the file's basis marker; a file without a readable marker gives every fact `unavailable/basis_unknown`.
- **Audited** per span: stored as field `audited`, unit `flag`, value 1 or 0, or `unavailable` when not stated.
- **Scaling:** values are taken as reported in the file's unit; a stated rounding level (lakhs, crores) is applied only when the fixtures show that the values are scaled; a unit that is not INR (or INR per share for EPS) is `unavailable/unit_unknown`.
- **Conflicts:** two facts for the same field, span, basis and context with different values are `unavailable/conflicting_facts`; a fact with any dimension other than the basis axis (segments) is skipped and counted.
- **Banking format:** recognised by its taxonomy; until a bank fixture is supplied every field is stored `unavailable/format_not_supported` (never a mapping guessed from the Ind AS names). Plan cut line: this stays the default if cut.
- **Sanity flags (never corrections):** `revenue > total_income`, EPS sign differs from PAT sign, `|eps_basic| < |eps_diluted|` for a profit; counted per run and returned by the facts endpoint.
- **Reading at `t`** (`IResultFactsAsOf.Facts(instrumentId, t)`): take every envelope of kind `results` of the instrument visible at `t` (P15-02 rule, lag included); for each `(period_end, field, span, basis)` the value comes from the **newest visible envelope that carries that basis** and was parsed by the current parser version. So a consolidated file following a standalone file of the same period (chained as a revision by P15-02) hides nothing of the standalone facts, and a true restatement of either basis replaces it only from its own visible time. Hand example: standalone Q1 broadcast 2026-07-24 16:12 IST, consolidated Q1 broadcast 16:40 IST, consolidated restatement 2026-08-30 10:00 IST; at 2026-07-24 16:30 only standalone facts; at 16:45 both; at 2026-08-30 10:05 the consolidated facts of the restatement and the original standalone facts.

## Acceptance criteria
1. **Real fixtures.** Given each of the five owner-supplied filings, then every stored fact equals the hand value read from the PDF of the same filing (exact decimal; EPS to the filed decimals); each field absent from the file is `unavailable` with a reason; the hand table is in the test with the PDF page cited. Until the five files exist the parser version is `xbrl-1-synthetic` (criterion 10).
2. **Synthetic fixtures.** Hand-made XBRL files (fake company, no real name) cover: quarter, half-year YTD, nine-month YTD, full year, both bases in one file, each basis alone, a missing EPS, a duplicate conflicting fact, a segment-dimension fact, a non-INR unit, a context ending on another date, an empty file. Each gives the stated facts and counts.
3. **Hostile input.** A DTD, an internal entity, a "billion laughs" expansion, an external entity or schema location, XML deeper than 64 levels, more than 200,000 elements, a file above 20 MB, invalid UTF-8 and a non-XML file each end in `result_parses.outcome = rejected` with a closed reason, no fact stored, no network call (a test handler that throws on any request), memory under 50 MB; `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`.
4. **Reshape migration.** `ReshapeResultFacts` first checks that `filings.result_facts` has **no row** (it raises and changes nothing otherwise), then replaces it with `result_facts(filing_id, parser_version varchar(24), field_code varchar(40), span varchar(4) in (q, ytd, fy), basis varchar(16) in (consolidated, standalone), value numeric null, unit varchar(16), state in (value, unavailable), unavailable_reason varchar(30) null)`, PK `(filing_id, parser_version, field_code, span, basis)`, check `state = 'value'` ⇔ `value IS NOT NULL`, check reason present ⇔ unavailable; and creates `filings.result_parses(filing_id, parser_version, outcome in (parsed, rejected, not_xbrl, no_facts), reason varchar(30) null, facts int, flags jsonb, parsed_at, PK (filing_id, parser_version))`. Both insert-only for `xd_app` (`GrantTableToAppRole` + `MakeAppendOnlyForAppRole`), nothing for `xd_exec` or the read roles (grants test per table). `Down` restores the P15-02 shape (also only when empty).
5. **Idempotent parse step.** In the `filings-ingest` run (still paused by default; "Run now" allowed), every `results` envelope with state `ok` **or** `unavailable` and no `result_parses` row for the current parser version is opened from the store folder (`IFilingFiles` has only `CopyAsync`, `UsedBytesAsync` and `FreeBytes` today: this slice adds a read-only `OpenAsync(sha256)` that streams the stored copy and fails `content_changed` when its hash differs), parsed and stored in one transaction per filing (`ON CONFLICT DO NOTHING`). A second run parses 0 filings. A `.pdf` envelope gets `not_xbrl`. A crash mid-file leaves no half set. A parse step failure marks the run failed and stops no other step.
6. **Per-basis read.** `IResultFactsAsOf` implements "Reading at `t`" above; the hand example passes at each instant; a property test (as P15-02 criterion 8) shows that adding, changing or removing any envelope or fact of a filing not visible at `t` leaves the answer at `t` byte-identical, with the control that a visible fact changes it; the answer does not depend on insert order.
7. **Unavailable is explicit.** The answer per field is `{state, value?, unit, reason?, filingId, broadcastTs}`; an undated envelope's facts are never returned (they stay counted); a field never seen is `unavailable/not_in_file`, never zero.
8. **API.** `GET /api/filings/{id}/facts` returns the parse outcome, the parser version and the stored facts with their states and sanity flags; 404 `filings.not_found`; no file content, no path.
9. **No decision use** (architecture test): `IResultFactsAsOf` is named only by its implementation, the facts endpoint and tests; the P15-02 allow-list is amended to name `ParseResultsHandler` as the only writer of `result_facts` and `result_parses`. Execution and `xd_exec` reference nothing new.
10. **Parser version.** `ResultsParser.Version` is `xbrl-1` only after criterion 1 passes; built and merged without the real fixtures it is `xbrl-1-synthetic`, and every consumer (P15-05) carries the version, so P15-10 reports `fu-1` as NOT_EVALUABLE "parser not checked against real filings" until it is `xbrl-1`. Same file, same version → identical facts (determinism test).
11. **Logging:** `filings.parsed|parse_rejected|parse_flags` carry counts, ids and codes only; never a value from the file, a path or an element text.

## Contract
- **API:** `GET /api/filings/{id}/facts` (`ApiJsonContext`; `scripts/gen-api.sh`); problem `filings.not_found`.
- **Data:** migration `ReshapeResultFacts` (criterion 4) with `Down`.
- **Jobs:** the parse step inside `filings-ingest`; no new service, no setting.
- **UI:** none (P15-11 shows coverage).

## Non-functional
Parse ≤ 1 s and ≤ 50 MB per 20 MB file; `IResultFactsAsOf` ≤ 10 ms per instrument with 5 years of filings; no network.

## Test plan
- **Unit (Domain):** span rule table, basis, scaling, conflicts, dimensions, sanity flags, the mapping table, the per-basis read with the hand example, determinism.
- **Fixtures:** five real (owner) and the synthetic set; the hostile set.
- **Integration (Timescale):** reshape migration up/down on an empty table and its refusal on a non-empty one, grants, idempotent parse step, the endpoint.
- **Architecture:** criterion 9; Domain has no I/O.

## Decisions (researched)
1. **Reshape the empty table rather than encode span in `field_code`.** The table has never been written; the span and the parser version belong in the key so a later parser can add its own facts without rewriting history. Rejected: `pat.q`-style codes (a hidden second key).
2. **Facts per basis from the newest visible envelope that carries it.** Fixes ADR 0180's limit without changing the store's supersede rule.
3. **Banking format unavailable until a bank fixture.** A guessed mapping would be silently wrong numbers.
4. **Sanity checks flag, never correct.** A corrected number is a number nobody filed.

## Risks and honest limits
- Without the owner's files the mapping is a guess; criterion 10 keeps that visible.
- Companies change taxonomy versions; an unmapped element yields `unavailable`, which may hide a field for some years (coverage shows it).
- One-offs and restatements distort later surprise measures (P15-00 risk 4); exceptional items are stored so P15-05 can show them, not remove them.

## Open questions (owner)
1. Supply five results filings (XBRL + PDF) into the inbox with sidecars? **Default if not done: built with synthetic fixtures, version `xbrl-1-synthetic`, `fu-1` NOT_EVALUABLE.**
2. Supply a bank filing? **Default: banking `unavailable/format_not_supported`.**

## Done when
- [ ] All acceptance criteria have evidence (criterion 1 needs the owner's files)
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any DTD or resolver path, any number stored without a unit or span, any guess for a missing field, any read of a non-visible envelope)
- [ ] Migration `ReshapeResultFacts` with `Down`; contract regenerated (`scripts/gen-api.sh`)
- [ ] ADR written; doc 20 §D (fact shape, span and basis rules) updated

## Build deviations (2026-11-02, see ADR 0184 "Deviations from the spec")
`basis_unknown` and the banking format are file-level (parse row `no_facts`, no fact rows); the `pat` fallback is a parse flag; `content_changed`/`unreadable` are retried and not stored; the per-basis read is per (period, span, basis); a stated rounding level gives `scale_unknown`.
