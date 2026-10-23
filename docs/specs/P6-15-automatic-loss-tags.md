# P6-15 — Automatic loss tags

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended in P6-00). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-15 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 2 d
- **Design refs:** docs/design/09-learning-engine.md §14 row 9 · ADR 0071 · ADR 0053 (MAE/MFE are over the whole trade) · ADRs 0065 to 0067 (cell source, rows)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **Descriptions, not features.** A tag is a function of the closed `SimTrade` alone (prices, excursions, slippage, timestamps up to the exit). It is never stored, never joins the feature catalogue, never reaches a decision. So no migration and no endpoint: a read-only verb `xd learn loss-tags` prints it.
2. **A loss is R below 0 in the cell's unit** (net when the cell is costed, else filled), the same unit as the cell statistics. Wins and flat trades are counted and not tagged.
3. **Tags built** (defaults, all printed and settable): `GapThroughStop` (stop exit at least 0.1 R beyond the initial stop level; a trailed stop that fills above it gives no tag), `GaveBackProfit` (MFE at least 1 R), `HighSlippage` (modelled fills at least 0.15 R worse than exact), `LateEntry` (the minute that closed the evaluation bar, i.e. decision instant minus one minute as in the trade key and the entry-window learner, is 14:30 India or later; trade left on its entry date and the fill is on the decision date, so an order that rested overnight is never late), `QuickStop` (stop within 10 minutes of the fill and MFE under 0.5 R), `WideStop` (stop distance at least 1.5 per cent of the entry), `NormalVariance` (a stop-out with exact R below 0 and not below -1.25 and no other tag; a stop-out that loses only the costs is untagged unless another rule fits). A loss can carry several tags; one that fits none is **untagged**, an ordinary outcome.
4. **Not built, listed in every report:** against regime (needs P6-02), news/results (needs the P15 event calendar), execution gap and decision drag (need real fills and recorded decisions), and "wrong time of day versus the cell" (the learned window of P6-14 is a proposal only; `LateEntry` is a fixed cut-off instead).
5. **Growing-tag detector.** Per cell, with at least 2 x W losses (W 30), order the losses by exit time and compare the last W with the W before: a tag grows when it is on at least 5 of the latest and its share rose by at least 0.2 (absolute). `NormalVariance` is never flagged. The result is a candidate lesson in the report; **no lesson is opened** because lessons are P6-16. The report states how many cell and tag pairs were compared, since some flags will be chance, only counts cells with at least 2 x W losses, and prints the expected number of chance flags (comparisons x an assumed 5 per cent).
6. No hold-out: tagging learns nothing and sets no parameter from the data.

## Goal
For the losses of stored runs, each loss shows which known signatures it carries, with the numbers that justify each, per-cell tag counts, and the tags that are growing.

## Scope
**In:** pure `Domain/Learning` (`LossTagger`, `LossTagAnalyzer`, options, report); `Application/Learning` query and handler `AnalyzeLossTags` (log ids 4530 and 4531); verb `xd learn loss-tags`; ADR 0071; docs.
**Out:** persistence, endpoint, screen, opening lessons (P6-16), the tags of assumption 4, using tags as features.

## Acceptance criteria
1. One fixture per tag: the tag fires at its threshold with the value shown and not just under it. *`LossTaggerTests`.*
2. A plain one-R stop-out is `NormalVariance` only; a loss that fits no rule is untagged; several tags come in a fixed order and `NormalVariance` steps aside.
3. A tag reads only the closed trade: changing strategy, symbol, snapshot and similar fields changes nothing. A gap is read only from a stop exit.
4. Counts per cell, wins excluded, untagged counted, the worst losses with their numbers. *`LossTagAnalyzerTests`.*
5. Growing detection fixture; too few losses or too small a rise flag nothing; determinism for any input order.
6. Bad input (no trades, duplicate key, NaN R, bad options) is a failure with a code.
7. The handler uses the cell unit for R, refuses a bad request before any read and an unknown run. *`AnalyzeLossTagsHandlerTests`.*
8. The verb parses and refuses bad lines. *`LearnLossTagsOptionsTests`.*
9. Architecture tests and `scripts/check.sh` green.

## Contract
- **API / Data / UI:** none. No migration.
- **Verb:** `xd learn loss-tags --run RUNKEY:STYLE:EXIT [--run ...] [--gap-min-r X] [--gave-back-mfe-r X] [--high-slippage-r X] [--late-after-minute N] [--wide-stop-fraction X] [--growing-window N] [--show-worst N]`; exit codes as the other `xd` commands.

## Non-functional
Pure, no clock or random, deterministic text. Logs carry counts and milliseconds only.

## Test plan
As the acceptance list; one hand-built trade per tag, 20-loss series for the detector.

## Honest limits
Replayed history, modelled fills. MAE/MFE are over the whole trade (ADR 0053), so "gave back profit" does not know the order of events inside the trade. Thresholds are judgement defaults, not fitted. Tags overlap and describe, they do not explain cause. Not exercised on the owner's real stored runs in this slice.

## Open questions
None blocking. For the owner: the thresholds, and whether `WideStop` and `QuickStop` (not in doc 09's list) should stay.

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] ADR 0071, doc 09, roadmap, site rebuilt
