# 0063 — The Lab report: built from the stored result, a deterministic summary and an inert page

- **Status:** Accepted (spec [P5-06](../specs/P5-06-verdict-and-report.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built on 2026-10-22). The text of this ADR has not been read by the owner.
- **Date:** 2026-10-22.
- **Relation to other ADRs:** [ADR 0041](0041-the-simulate-verb-the-export-library-and-the-pipeline-in-the-host.md) (export library: HTML encoder, manifest), [ADR 0059](0059-the-test-ledger-and-the-verdict-evaluator.md), [ADR 0060](0060-the-full-test-walk-forward-over-timeframe-side-and-exit.md), [ADR 0062](0062-robustness-sensitivity-monte-carlo-by-year-and-a-sign-flip-baseline.md).

## Context
Doc 11 §4 lists a report: verdict, KPIs, best cells, by instrument, charts, comparisons, warnings, trades, summary. The stored result (`lab.lab_results`) holds the verdict, gates, evidence, cells, exploration block and robustness; not win rate, rupees, per-instrument tables or trades.

## Decision
1. **Built from the stored result only.** `GetLabReportQuery` reads the run and its result and `LabReportBuilder` (pure) makes `LabReportView`. A succeeded `full` or `robust` run has a report; any other run is 409 `lab.no_report`, an unknown one 404. Sections the document cannot fill are listed in `notIncluded`, not invented.
2. **Summary is a fixed template** (`ReportSummary`): raw report numbers only (counts, or figures printed with up to two decimals), no date, clock or assistant; it quotes the outcome and never advises. A property test checks every number it prints against the report's own values; a golden text pins the wording.
3. **Warnings** (`low_samples`, `cost_sensitivity`, `concentration`, `unstable_parameters`, `chance_not_excluded`) are rules with constants printed in the text (30 trades per cell, 0.15 R net expectancy, 60 % of trades in one cell or one year above the total, p and share-without-edge 0.05). They never change the outcome.
4. **Honest limits are always present:** the run window and "about 2.1 years, no hold-out", folds against the minimum for PASS, the exploration label, the robust block's limits and the lenient sign-flip baseline, and the limits stored with the verdict.
5. **Three representations:** JSON (`GET /api/lab/runs/{id}/report`), one static page (`/report/html`), and a zip folder (`/report/folder`: `report.json`, `report.html`, `summary.txt`, `README.txt`, `manifest.json`). The page uses the export library's `Html.Text`/`Html.Attr` (strips control and bidi characters, escapes), has no script, link, image or frame, and a Content-Security-Policy meta and header. The folder uses the library's `ExportManifest` (format `xd-lab-report-1`, run key, SHA-256 per file) and fixed zip entry times, so equal reports give equal bytes. The renderers live in `XDrishti.Api` because the library references the Domain only.

## Consequences
- No migration; OpenAPI and frontend types regenerated (`LabReportView` and parts).
- Adding win rate, rupees, per-instrument tables, charts or the trade list means storing more in the full test's document first (a later slice); the `notIncluded` list shrinks then.
- The folder does not contain the trades; the run section lists the keys of the candidate runs in `research.sim_runs`.
