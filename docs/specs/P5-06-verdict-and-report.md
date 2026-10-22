# P5-06 — Verdict and report

- **Status:** Approved (owner's standing run instruction of 2026-10-22, "proceed without waiting": every open question below is taken as recommended). Depends on [P5-05](P5-05-robustness-sensitivity-monte-carlo-by-year-random-baseline.md) (built, ADR 0062), [P5-04](P5-04-full-test-walk-forward-over-timeframe-side-exit.md) (ADR 0060) and [P5-03](P5-03-test-ledger-and-the-gates.md) (ADR 0059).
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice 6 of [P5-00](P5-00-strategy-lab-plan.md).
- **Design refs:** docs/design/11-strategy-lab.md §4 (Report), §5 · ADR 0041 (export library) · ADRs 0059, 0060, 0062.

## Assumptions (taken as the recommended answers; none is an owner-only decision)
1. **The report is built from the stored result only** (`lab_runs` row and the `lab.lab_results` document). No second engine pass, no new table, no migration. Sections of doc 11 §4 whose numbers the document does not hold (win rate, average win/loss, net rupees, by basket and by instrument, the charts, the trade list with replay) are **not invented**: the report lists them under `notIncluded` with the reason (the trades are the candidate runs in `research.sim_runs`, whose keys the run view already lists). Adding them is a later change to what the full test stores.
2. **Which runs have a report:** a succeeded run of method `full` or `robust` (the ones with a result). Anything else is 409 `lab.no_report` (quick test, queued, running, failed, cancelled); an unknown run is 404.
3. **Endpoints:** `GET /api/lab/runs/{id}/report` (JSON), `GET /api/lab/runs/{id}/report/html` (one static page), `GET /api/lab/runs/{id}/report/folder` (a zip "folder": `report.json`, `report.html`, `summary.txt`, `README.txt`, `manifest.json`). The folder is a zip with fixed entry times, so equal reports give equal bytes. The page and the manifest reuse the export library of ADR 0041 (`Html.Text`/`Html.Attr`, the one place a dynamic string becomes HTML; `ExportManifest` with its SHA-256 per file); the renderer lives in the Api host because the library references the Domain only and the report type is an Application type.
4. **The summary is a deterministic template** over report numbers: no assistant call, no clock, no random source. Every number in it is a raw value of the report (a count, or a figure rounded to 2 decimals), none is computed from others; a test checks this as a property over random reports and with a golden text. Wording never says "will", "profitable" or advises; PASS/MARGINAL/FAIL is quoted, not re-judged.
5. **Warnings** are rule-based over the same document (thresholds are Domain-free constants of the report builder, printed in the warning text): `low_samples` (the `oos_trades` gate missed, or a cell with fewer than 30 out-of-sample trades), `cost_sensitivity` (out-of-sample expectancy under 0.15 R net of costs, so a small rise in costs erases it), `concentration` (one cell holds at least 60 % of the out-of-sample trades, or one year holds more than the whole total R in a robust run), `unstable_parameters` (a sensitivity parameter that is `fragile`, `no_base_edge` or `inconclusive`, or a cell whose exit choice was unstable), `chance_not_excluded` (sign-flip p-value above 0.05 or at least 5 % of Monte Carlo draws without edge). A warning never changes the outcome.
6. **Honest limits** are always present and not filterable: the data range of the run window and "about 2.1 years of data, no hold-out" (ISS-19), the number of folds against the minimum for PASS, "full-period figures are in-sample exploration", the exploration labels of the result, and for a robust run "the sign-flip baseline is lenient" plus the limits of the block; limits stored with the verdict are carried through.
7. **HTML is inert:** no script, no link, no image, a Content-Security-Policy meta and header `default-src 'none'; style-src 'unsafe-inline'`, every dynamic string through `Html.Text`. Untrusted text reaches the report through strategy id, universe label and exception texts of refused sweep points.

## Goal
`GET /api/lab/runs/{id}/report` answers, from numbers already stored: what was the verdict and why, how much evidence is behind it, where it works, what to be careful about, what this test cannot tell, and a few plain sentences. The same content is a static page and a downloadable folder.

## Scope
**In:** `LabReportView` and parts (Application), pure `LabReportBuilder` and `ReportSummary`, `GetLabReportQuery`/handler, error `lab.no_report`, three endpoints, the HTML renderer and zip writer in `Api/Lab`, `ApiJsonContext`, ADR 0063, docs 11 and 16 not needed (no config), roadmap row, `gen-api`.
**Out:** charts, trade list, per-instrument and per-basket tables (assumption 1); an assistant-written summary; the report screen (P5-08b); promotion.

## Acceptance criteria
1. Given a succeeded `full`/`robust` run, `GET .../report` returns the report with verdict, gates, KPIs, best cells (by out-of-sample mean R, descending), exploration block, robustness (or null), warnings, honest limits, `notIncluded`, labels and summary; unknown run 404; any other run 409 `lab.no_report` (unit and integration).
2. The summary is deterministic, quotes the outcome and the report's own numbers only (property test over random reports: every number token in the summary equals a raw report value formatted the same way; golden text for one fixed report).
3. Each warning rule fires on a hand-made document and stays quiet on a clean one (unit).
4. The honest limits are present for every report, include the run's data range, the fold count against the minimum, and for a robust run the sign-flip line (unit).
5. The HTML page escapes untrusted text (a strategy id or label with `<script>`, quotes and a bidi control comes out inert), has no script, link, image or frame, and carries the CSP (unit); the same bytes for the same report.
6. The folder is a zip of the five files; `manifest.json` lists the sha256 of the other four and they match; two calls give identical bytes (unit/integration).
7. Endpoints need the owner session; no strategy text, trade or secret in a log line; Architecture tests green.
8. `scripts/gen-api.sh` run; OpenAPI and frontend types committed.

## Contract
- **API:** `GET /api/lab/runs/{id}/report` -> `LabReportView`; `.../report/html` -> `text/html`; `.../report/folder` -> `application/zip` (attachment `lab-report-<id>.zip`). Problems: 404 `lab.not_found`, 409 `lab.no_report`.
- **Data:** none. **UI:** none (P5-08b). **Jobs:** none.

## Non-functional
Reads one run row and one result row; building is in memory and linear in the size of the document (kilobytes). Deterministic. Logs: nothing new beyond the request log.

## Test plan
Unit (Application): builder (sections, ordering, warnings, limits), summary (golden, property). Unit (Api renderer, in the integration project or Application tests as the layer allows): escaping, CSP, zip determinism and manifest. Integration: report endpoints on a finished robust run, 404, 409 for a quick run, 401 anonymous.

## Honest limits of this slice
The report carries no profit in rupees, no win rate and no trade list because the stored result does not; it says so.

## Open questions (all taken as recommended)
1. Build from the stored result or re-run for extra figures? Recommended: stored result only. 2. Zip or a folder on disk? Recommended: zip stream (no server-side files). 3. Should warnings change the outcome? Recommended: no.

## Done when
- [x] Questions answered, status Approved
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green
- [ ] `scripts/gen-api.sh`; ADR 0063; roadmap; doc 11; `scripts/build-docs.sh`
