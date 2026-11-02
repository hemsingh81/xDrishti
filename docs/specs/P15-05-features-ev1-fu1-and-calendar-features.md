# P15-05 — Features `ev-1` and `fu-1` and the frame event features: point-in-time extension rows per trade, `days_to_results` and `days_since_results` readable by rules, NaN for every unknown

- **Status:** Draft (written 2026-11-02 under the owner's delegation; the owner's Approved is needed before an agent builds it)
- **Phase / roadmap row:** P15 — slice P15-05 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Features `ev-1` and `fu-1`").
- **Design refs:** docs/design/20 §D · 09 §3 (features), 11 §2 (the frame) · 07 §1 · ADR 0033 (date features come from the caller), 0066 (side tables, no imputation), 0037 (engine version, run key), 0180 (store the lag), 0181 (`days_to_results`), 0182 (the snapshot) · P13-00 D5 (extension side table, not `tf-2`) · [P13-05](P13-05-feature-set-comparison-core.md) (owner of `research.trade_features_ext` and its store). New ADR: **next free number at build time**, plus an ADR 0037 amendment (engine `sim-8`) and a doc 11 change (two frame features).
- **Needs:** P15-03 (facts; for `fu-1` only), P15-04 and P15-06 (built: `IResultsCalendar`, `IShareholdingAsOf`, `IResultsFilingTimes`, `EventCalendarSnapshot`), P1-10a (adjusted prices, applied corporate actions), the stored market context (NIFTY 50 daily close). **P13-05** (spec Draft; built before this slice): it creates `research.trade_features_ext` and `IExtensionFeatureStore`, which this slice only uses (Decision 1). **Needed by:** P15-09 (rules read `days_since_results`), P15-10 (protocol X).
- **Size:** 3 agent-days (estimate; the plan said 3; +0.5 for the frame features in the simulator with their engine bump, −0.5 because the features table and its store are P13-05's).
- **Safety:** no — research features on stored runs and two frame features a strategy may name. It changes no plan, selection, check or order path; a strategy that does not name the new features runs exactly as on `sim-7` (differential test).

## Goal
For every trade of a stored research run, the system can store, as of its decision instant and only from what was public then, the event features `ev-1` and the fundamental features `fu-1` in an extension side table, so protocol X (P15-10) can compare `tf-1` against `tf-1` plus each set. A strategy rule can read `days_to_results` and `days_since_results` as frame features. Every value that cannot be known is NaN and counted, never imputed.

## Verified facts (read 2026-11-02)
**Code:**
- `TradeFeatureCatalog.Version = "tf-1"` (36 stored + 5 cell features, `research.trade_features`); `TradeFeatureExtractor` (Simulation/Scanning) extracts it; `TrainingDataBuilder`, `WalkForwardValidator`, `HoldOut`, `MultipleTesting.Factor` exist. **No `research.trade_features_ext` or `research.extension_evaluations` table exists**; both are created by P13-05 (spec Draft 2026-11-02).
- `ResultsDays.Compute` gives the sessions to the **next** results event only; nothing gives "sessions since the last results". `IResultsFilingTimes.ReadAsync` returns the broadcast times (state `ok`) of stored `results` envelopes (P15-06, the loss tag). `IShareholdingAsOf.Latest` returns the current group and the one exactly one quarter earlier.
- The frame (`XDrishti.Indicators`, `FrameOptions`) takes date-driven inputs from the caller (`ExpiryDays`, `Context`); `DateFeatureKind` lists the date features; the frame sees a context day "from the next date on". The strategy kernel is three-valued (Kleene): a comparison with NaN is *unknown* (`Kernel`), so `not (days_to_results <= 2)` on NaN is unknown, not true.
- `EventCalendarSnapshot.Answer` recomputes per call; its comment says a per-bar caller must precompute intervals. `SimulationEngine.Version = "sim-7"`.
- `ICorporateActionQueries.ListAppliedAsync(instrument)` returns applied actions by ex-date.

## Scope
**In:**
- Domain (pure): `EventFeatureCatalog` (`ev-1`), `FundamentalFeatureCatalog` (`fu-1`), `SurpriseMath` (SUE, TTM, growth, margin), `ReactionSession`, `EpsAdjustment`, `ResultsDaySeries` (the per-date frame series for one instrument).
- Application: `ExtensionFeatureExtractor` (reads through `IResultsCalendar`/snapshot, `IResultsFilingTimes`, `IResultFactsAsOf`, `IShareholdingAsOf`, bars, context and corporate actions, all at or before the decision instant); verb `xd learn features --extension ev-1|fu-1 --run <key>`.
- Migration `AddExtensionExtractions`: `research.extension_extractions` only. Rows of `research.trade_features_ext` (P13-05's table) are written through P13-05's `IExtensionFeatureStore` (extension `ev` / `fu`, versions `ev-1` / `fu-1`).
- Indicators: `DateFeatureKind.DaysToResults`, `DaysSinceResults`; `FrameOptions.ResultsDays` (neutral types, no Filings type in Indicators).
- Simulation: the snapshot and a filings-time list handed to a run whose strategy reads either feature; engine `sim-8`.
- Docs: doc 11 (frame features), doc 09 (extension ids), ADRs.

**Out:** evaluation (P15-10); the PEAD bucket features and the hold limit (P15-09); any use in scoring, selection or the plan; a composite quality score (P15-00 X: none is built); analyst estimates; the LLM flag `llm-1` (P15-08 defines it); a screen.

## The features
**Reaction session `R`** of a results filing: the first trading session whose open (09:15 India time) is at or after `broadcast_ts + lag`. Hand example: broadcast Friday 2026-07-24 16:12 IST → `R` = Monday 2026-07-27; broadcast Monday 2026-07-27 11:00 IST → `R` = Tuesday 2026-07-28.

**`ev-1`** (at decision instant `t`):
| Feature | Definition |
|---|---|
| `days_to_results` | P15-04 `ResultsDays` at `t` (sessions, 0 on the event session); NaN when `unavailable` (any reason) |
| `days_since_results` | sessions from `R` of the newest `results` envelope visible at `t` to the India date of `t` (0 on `R`); NaN when none is visible or `R` is after the date of `t` |
| `results_car3` | for the newest visible results filing whose `R` close is at or before `t`: the stock's close-to-close return over the three sessions ending at the close of `R` (`close(R) / close(R−3) − 1`, adjusted closes) minus the NIFTY 50 return over the same sessions; NaN when any close is missing |

Back-adjusted closes are safe here: a ratio of two adjusted closes is unchanged by any later action, so no later information enters.

**`fu-1`** (EPS basic, span `q`; one basis per instrument: consolidated when all quarters the feature needs have it, else standalone when they all have it, else NaN; EPS of earlier quarters multiplied by the split and bonus factors of applied actions with ex-date after that quarter's period end and at or before `t`):
| Feature | Definition (quarter `q` = newest visible results quarter) |
|---|---|
| `sue_yoy` | `(EPS_q − EPS_{q−4}) / sd(previous 8 such differences)`; needs 13 quarters; NaN when sd = 0 |
| `sue_ttm` | `(TTM_q − TTM_{q−4}) / sd(previous 8 such differences)`, `TTM_q` = sum of EPS `q−3..q`; needs 16 quarters |
| `rev_growth_yoy` | `revenue_q / revenue_{q−4} − 1`; NaN when `revenue_{q−4} ≤ 0` |
| `margin_chg_yoy` | `pat_q / revenue_q − pat_{q−4} / revenue_{q−4}` in percentage points |
| `promoter_pledge_pct` | `pledged_pct` of the current visible promoter row |
| `promoter_chg_qoq` | promoter `pct` minus the previous quarter's (only when exactly one quarter earlier, P15-04) |
| `pe_pctile_5y` | `close(t's previous session, unadjusted) / TTM EPS adjusted to t's share basis`, ranked within the same instrument's daily P/E of the previous 1,250 sessions (needs ≥ 1,000 values; NaN when TTM EPS ≤ 0) |

Hand example (`sue_yoy`, P15-00 P): EPS 12.0 against 10.0 a year earlier with sd 1.0 of the previous eight differences → 2.0; with a 1:2 split (ex-date after the year-ago quarter) the year-ago EPS of 20.0 is first adjusted to 10.0.

**Frame features** (readable in a strategy): `days_to_results` and `days_since_results` as above, but per **India date** and as known at the **start of that date** (rows and filings visible at 00:00 IST of the date): a meeting intimated at 11:00 on a date counts from the next date. This is never earlier than the instant rule of `not_event_day`, so it can only see less. The caller precomputes the series per instrument (`ResultsDaySeries`) once per run; the frame reads a dictionary. NaN on a date is *unknown* to every rule, so no rule passes on a missing calendar.

## Acceptance criteria
1. **Point in time (the guard).** Property test: for random filings, calendar rows, shareholding groups and facts, changing, adding or removing any input not visible at the decision instant (broadcast or intimation `+ lag > t`, undated rows, bars after `t`) leaves every `ev-1` and `fu-1` value byte-identical; control: changing a visible input changes at least one value; insert order does not matter. At least 200 cases per property, fixed seed.
2. **Hand values.** The reaction-session, `results_car3`, `sue_yoy` (with and without the split), `sue_ttm`, growth, margin, pledge, promoter change and `pe_pctile_5y` cases above and a holiday `R` equal hand values to 1e-12.
3. **NaN, never imputed.** Every feature with missing input is NaN and the extraction record counts NaN per feature and per reason (`no_filing`, `parser_unverified`, `basis_mixed`, `short_history`, `no_calendar`, `no_context`, `no_bars`); a feature is never filled from a neighbour, a sector or a default. Facts from parser version `xbrl-1-synthetic` are read and labelled (`parser_unverified` count), never silently treated as checked.
4. **Storage.** Feature rows go to `research.trade_features_ext` (created by P13-05, columns there: `feature_values`, PK (run_key, trade_key, extension, version)) through `IExtensionFeatureStore`, `ON CONFLICT DO NOTHING`. Migration `AddExtensionExtractions` creates `research.extension_extractions(run_key, extension, version, lag_minutes, parser_version, calendar_sha256, filings_watermark timestamptz, rows, nan_counts jsonb, extracted_at, PK (run_key, extension, version))`, insert-only for `xd_app` (table by table), nothing for `xd_exec` or the read roles (grants test); `Down` drops it only. Catalog versions `ev-1` and `fu-1` with their ordered names are written in code and in the extraction record.
5. **Extraction verb.** The existing `xd learn features --run <key>` (P6-04, `tf-1`) gains `--extension ev-1|fu-1`; without the flag it behaves exactly as today (test). With it, it reads the trades of a stored run, extracts every row at its decision instant, stores rows and one extraction record; a second call stores nothing and says so. The record stores the lag, the parser version, the calendar snapshot hash and the newest broadcast time read (`filings_watermark`), so P15-10 can tell when inputs changed.
6. **Frame features.** `DateFeatureKind.DaysToResults` and `DaysSinceResults` are in `IndicatorCatalog`; a frame asked for either without `FrameOptions.ResultsDays` is refused at `Create` (`options.resultsDays`), as `is_expiry_day` is. Hand cases: the P15-06 window table (meeting 2026-08-12) gives frame `days_to_results` 3 on 08-07 when the intimation was visible before 08-07, and NaN on 08-03 for an intimation at 08-03 11:00 (visible from 08-04). A rule `not (days_to_results <= 2)` on a NaN date takes **no** entry (test through the compiled kernel).
7. **Simulator and engine (ADR 0037).** A strategy naming either feature receives the precomputed series built from the run's snapshot and filings times by one read before the walk (the walk does no I/O); with no calendar row and no results filing in the window it is refused `simulation.filter_not_available` ("no results calendar or filings for the window (P15)") before any bar is read. `SimulationEngine.Version` becomes `sim-8`; the run key adds the filings-time hash when the features are read; goldens are regenerated on purpose with the label diff only; strategies that do not read the features equal `sim-7` trades (differential test); `RunAssemblyTests` maps `sim-8`.
8. **No decision use** (architecture test): the extension catalogs and `ExtensionFeatureExtractor` are named only by the verb, tests and (later) P15-10; Execution, Selection, Planning, the checklists and the scorer read neither them nor `research.trade_features_ext`; the P15-02/04/06 allow-lists are amended to name the extractor as a reader. The no-order tripwire is green.
9. **Determinism.** Same run and inputs in any order → identical rows and record.

## Contract
- **API:** none new (P15-10 reads the tables; P15-11 shows counts through `/api/research/extensions`).
- **Data:** migration `AddExtensionExtractions` (criterion 4) with `Down`; rows in P13-05's `research.trade_features_ext`.
- **Verb:** `xd learn features --extension ev-1|fu-1 --run <key>`.
- **Engine:** `sim-8`, run-record schema +1 if a new counter is added.

## Non-functional
Extraction ≤ 20 ms per trade (batched reads per instrument); the per-date series for 214 instruments over 5 years ≤ 2 s; the walk's speed for strategies without the features unchanged (benchmark ≤ 2 %).

## Test plan
- **Unit (Domain):** every feature's hand table, reaction session around weekends and holidays, EPS adjustment, the basis rule, NaN reasons, the frame series rule (start-of-date visibility).
- **Property:** criterion 1 and 9.
- **Simulation:** differential test against `sim-7`, refusal without inputs, run key change, goldens.
- **Integration (Timescale):** migration, grants, idempotent verb on a stored fixture run.
- **Architecture:** criterion 8; Indicators references no `Filings` type.

## Decisions (researched)
1. **This slice uses `research.trade_features_ext`; P13-05 owns it** (Review fixes (3) of P15-00 and P13-00). P13-05 needs no recordings, so it is built before this slice; one owner of the schema and its grants test. Only `research.extension_extractions`, whose columns are filings-specific, is created here.
2. **`days_since_results` from results envelopes, `days_to_results` from the calendar.** A past meeting date says less than the broadcast that actually happened.
3. **`results_car3` is the three sessions ending at the close of `R`.** It is known at that close; the plan's "announcement window" did not say which sessions, and a window reaching past `R` would leak into an entry at `R`'s close.
4. **Frame features by start-of-date visibility.** Cheap (a dictionary per date), never earlier than the instant rule, consistent with the context features.
5. **EPS adjusted for splits and bonuses known by `t`.** An unadjusted year-ago EPS would make every split a huge surprise.

## Risks and honest limits
- `pe_pctile_5y` needs 1,000 sessions of prices before `t`: with bars from 2024 it is NaN until about 2028, unless CF-04 Part A (daily history) lands.
- `sue_yoy` needs 13 quarters of filings and `sue_ttm` 16: they are NaN until the owner's files go back that far (XBRL before 2018 UNVERIFIED).
- Market adjustment uses NIFTY 50, not a sector index; beta is not removed.
- Survivorship (ISS-22) and gross R (ISS-25) carry into every comparison.

## Open questions (owner)
1. ~~Create the extension features table here instead of in P13-05?~~ Resolved 2026-11-02: no, P13-05 owns it (Decision 1).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any input read after the decision instant, any imputation, any change for strategies without the features, any decision-path reader)
- [ ] Migration `AddExtensionExtractions` with `Down`; goldens regenerated on purpose
- [ ] ADR and the ADR 0037 amendment (`sim-8`); docs 09, 11 and 20 §D updated
