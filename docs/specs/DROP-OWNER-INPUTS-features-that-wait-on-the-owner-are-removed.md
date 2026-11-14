# DROP-OWNER-INPUTS — Features that wait on the owner are removed

- **Status:** Approved (owner, 2026-11-14, in chat: "Drop every owner-input feature"; Q1 both automatic, Q2 gross forever, Q3 SR-03 removed)
- **Phase / roadmap row:** cleanup, next to DROP-P13 (ADR 0288) and DROP-BASIS (ADR 0289)
- **Decisions:** ADR 0291 (to be written at build)
- **Design refs:** docs/design/05-data-and-brokers.md (market context), 08-exits-and-risk.md (costs), 09-learning-engine.md (hold-out), 11-strategy-lab.md; specs P3-01c, P3-01d, P3-lift, P3-03c, P3-03f, P8-23, SR-03, P9-09

## Goal
Nothing in xDrishti waits on data or checks the owner has to supply. The owner has already given all the history he has (the 1m bars of 313 instruments, 2024-04-01 to 2026-11-13). Every feature that can only work with more owner input is removed: code, settings, UI, tests and tables. Plans and docs stop listing it as an owner to-do.

## Facts this spec is built on (main at a6b1ed42, live DB 2026-11-14)
- `market.context_daily` has **0 rows**. The NIFTY 50 / INDIA VIX context has never been ready, so every context feature already reads "not ready" (a false condition). Removing it changes no result produced so far.
- `market.basket_context_daily` also has 0 rows, but it is computed from member bars (BK-06). It needs no owner input, so it **stays**.
- The context code reaches about 30 non-migration files: Domain/Application/Infrastructure `Market/Context`, `Api/Context`, 5 files in `XDrishti.Indicators` (regime / VIX features), Learning feature extraction, Replay (`IContextFigures.AsOfAsync`), Planning (`StoredPlanContextSource`), Lab, Reporting and the AI read-only data reader.
- The charge-sheet code (ISS-25) reaches about 60 files: `Application/ChargeSheet`, Domain/Application/Infrastructure `Paper` and `Reporting`, `Replay/HoldOut` (SR-03 registration), Learning extensions, CLI verbs, the settings screen and the simulation replay screen.

## What is removed

| # | Item | Waits on | Removed | Kept |
|---|---|---|---|---|
| R1 | NSE CSV context inbox for NIFTY 50 / INDIA VIX (P3-01d fallback D, ADR 0290, merged c60209d6 on 2026-11-14 before this decision reached it) | owner CSV files | the inbox loader, its settings, folder, log events and tests; ADR 0290 marked superseded | — |
| R2 | Market context: NIFTY 50 / INDIA VIX daily (P3-01c context part, P3-01d job, P3-lift) | owner's live Dhan probe | `context_daily` table (NEW down migration), context data job, `IContextFigures`, `ContextOptions`, the ten context features in Indicators, regime basis `index_and_vix`/`index_only`, `data_context_stale` notice, context API, AI reader tool | weekly/monthly timeframes and calendar features (P3-01c); basket context (BK-06) |
| R3 | Charge-sheet confirmation (ISS-25, P3-03f stored sheet, P8-23 writer) | owner marks the sheet `checked` against a contract note | see Q2 | see Q2 |
| R4 | SR-03 pre-registered replay over a hold-out block | R3 (`checked` sheet) | see Q3 | — |
| R5 | Owner to-dos in roadmap/memory for R1–R4 | — | removed from `docs/design/18-roadmap.md` and the owner list | — |

Strategies, cells or plans that read a removed context feature: none can have fired on it (the feature was never ready). Any strategy text naming one fails `POST /api/lab/validate` after the change; the build lists them and the seed strategies are fixed in the same change.

## Acceptance criteria
1. **No context code.** No type, setting, job, endpoint, table or UI element for the NIFTY/VIX context remains. An architecture test pins that no assembly references a `Market.Context` index/VIX type.
2. **Migration.** A NEW migration drops `market.context_daily` (never edits an old one); a test pins its presence, as DROP-P13 does.
3. **Same results.** A golden run of the P3 pack and one SR-01 replay give the same result hash before and after the change (the context was never ready).
4. **Validate.** A strategy naming a removed context feature answers 422 with the feature named; no seed strategy names one.
5. **Costs (per Q2).** Every run, report and replay labels its cost basis; no screen or verb asks the owner to check a sheet.
6. **Docs.** Design docs, the specs listed above and the roadmap mark the dropped parts `Dropped (ADR 0291)`; `docs/site` is not regenerated.
7. `scripts/check.sh` green; `scripts/gen-api.sh` run; Opus reviewer has no blockers.

## Test plan
Architecture tests (no context references; no charge-sheet check path per Q2), migration presence test, golden-hash tests (P3 pack, SR-01), validate test for a removed feature, frontend tests for the removed settings/replay elements.

## Open questions (each with a recommendation)
1. **ANSWERED 2026-11-14: both automatic.** Hold-out block 1 look and the eval baseline re-accept are also owner acts. They are decisions, not data, so they are **not** in this drop. Recommendation: keep them, but switch both to automatic under the ADR 0282 rule where the setting allows (an auto look forfeits nothing new; the eval baseline is re-accepted automatically only when no threshold regresses). Confirm.
2. **Costs — ANSWERED 2026-11-14: (b) gross forever.** Options were:
   - (a) **Recommended:** keep the cost engine and replace the owner's `checked` step with a built-in charge sheet from the published NSE / SEBI / Dhan tariffs (brokerage, STT, exchange, SEBI fee, stamp duty, GST), versioned with its source and date, marked `published_tariff`. Runs can be net again with no owner input. Owner check code (P8-23 writer, `checked` state, CLI verbs) is removed.
   - (b) Remove the net path entirely: everything stays gross forever; `CostChoice`, the stored sheet, net R and the bill check are removed.
3. **SR-03 — follows Q2 (b): removed.** With Q2 (a) it registers automatically and stays. With Q2 (b) it is removed (code, tables via a NEW down migration, UI card). Recommendation: follows Q2 (a), keep.

## Build state (2026-11-14)
- R1 and R2 (the market context and the CSV inbox) are built and merged-ready on `feature/drop-owner-inputs`: migration `DropMarketContext`, the removal test (`DropMarketContextMigrationTests`), the architecture pin (`ContextDroppedArchitectureTests`), the validate test (`A_removed_market_context_name_is_refused_as_an_unknown_name_that_is_quoted`).
- R3 (the charge sheet, Q2) and R4 (SR-03, Q3) are decided and **not built**: about 180 source files and their tests, two more migrations; see the build handover in the ADR 0291 thread.
- R5 (owner to-dos) is open for the Q1 items and R3/R4.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] ADR 0291 written; design docs and roadmap updated
