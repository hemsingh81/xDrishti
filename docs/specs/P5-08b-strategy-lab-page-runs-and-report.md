# P5-08b — Strategy Lab page: runs, verdict, report and ledger

- **Status:** Approved (owner's standing run instruction, 2026-10-22: questions take the recommended answer).
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice P5-08b of the plan [P5-00](P5-00-strategy-lab-plan.md); depends on P5-02 to P5-06 (built, ADRs 0058 to 0063) and P5-08a.
- **Design refs:** docs/design/11-strategy-lab.md §3, §4, §5; docs/prototype (Strategy Lab page: run buttons, progress, Result card with verdict and gates, ledger table); P5-02, P5-03, P5-06 Contracts.

## Assumptions (recommended answers taken under the standing instruction)
1. **Frontend and docs only.** No API change, no regenerate. The hub transport gains two handlers (`labRunProgress`, `labRunFinished`) because the generated contract already names the events but the client wired only prices and feed status.
2. **What is tested.** Only a stored version that is open in the library (a draft must be saved first), so a run always points at an immutable version.
3. **Universe.** A basket (read from `GET /api/baskets`) or typed symbols (split on spaces, commas, semicolons); the server resolves and validates. The window is the server default (last year of stored data); a window picker is left out.
4. **Methods.** Quick, full, robust, with a one-line hint each. Costs are mandatory for full and robust: a refusal (`422 lab.invalid_input`) is shown as the server's message.
5. **Live progress.** The page holds the hub while open; a progress event is written into the cached run and run list, the end event reads runs, ledger again. When the hub is not connected the run and the list are read every 3 seconds while a run is `queued` or `running`. A missed event costs nothing (the REST view carries the same data).
6. **Verdict screen** is built from `run.verdict` (outcome as text and chip, variant count and tightening factor, every gate with required, adjusted, actual, Met/Missed and its reason, honest limits always shown, labels). A quick test has no verdict; its labels say it is exploration.
7. **Report** (`GET /api/lab/runs/{id}/report`) only for succeeded full/robust runs: summary, KPIs, warnings, honest limits, best cells, exploration (labelled in-sample), robustness block, "not in this report"; links open the HTML page (new tab) and download the zip folder. Charts are left out (the report has none in its contract).
8. **Ledger** of the open family. A row of method `sweep` has a derived id that is not a run, so it shows "parameter sweep point" and no link (ADR 0062); other rows have "Open run".
9. **Promote** stays absent (P6). No form builder.

## Goal
From the Strategy Lab page the owner starts a quick, full or robust test of the open version on a basket or symbols, watches it live, can cancel it, reads the verdict with every gate and the honest limits, opens the report (page and zip), and sees the family's test ledger.

## Scope
**In:** start-run form; runs list with state; run detail with live progress, cancel, problems, labels, counts; verdict and gates; report view and links; ledger view; hub handlers for the two Lab events; tests.
**Out:** promote (P6), window picker, charts, comparing runs, deleting runs.

## Acceptance criteria
1. Given no stored version is open, then "Run test" is disabled; given one is open and symbols or a basket are chosen, then it posts `{strategyId, version, method, universe}` and the new run is selected.
2. Given the server refuses a start (422/409/404), then its message is shown and nothing is selected.
3. Given runs exist, then they are listed newest first with method, universe, time, state and verdict outcome; loading and error states are shown.
4. Given a running run and the hub connected, when `labRunProgress` arrives, then the progress text updates; when `labRunFinished` arrives, then the run is read again and shows its final state.
5. Given the hub is not connected, then a running run is polled until it ends.
6. Given a running run, when Cancel is pressed, then `POST …/cancel` is sent and "cancel requested" shows; a refusal shows its message.
7. Given a run with a verdict, then outcome, variant count, adjustment factor, each gate (required, adjusted, actual, Met/Missed, reason) and the honest limits are visible, with status as text.
8. Given a succeeded full or robust run, then the report is loaded (loading and error states) and shows summary, KPIs, warnings, honest limits and links to the HTML page and the zip; a quick test shows no report.
9. Given a family is open, then its ledger lists rows with the variant count and factor; real runs have "Open run"; sweep rows have no link.
10. `scripts/check.sh` is green.

## Contract
- **API:** uses `GET/POST /api/lab/runs`, `GET /api/lab/runs/{id}`, `POST …/cancel`, `GET …/report`, `GET …/report/html`, `GET …/report/folder`, `GET /api/lab/strategies/{id}/ledger`, `GET /api/baskets`; hub events `labRunProgress`, `labRunFinished`.
- **Data:** none.
- **UI:** `features/lab` on the `/lab` route (lazy since P5-08a): cards "Test runs" (form and list left, detail right) and "Test ledger" below the editor; hue `lab`.
- **Events/jobs:** none.

## Non-functional
Accessible: labelled form fields, progress bar with a label and a status line, gate result as text, tables with names. No new dependency; the lazy chunk grows by the run components only. Hub connection is held only while the Lab page is open.

## Test plan
Vitest + Testing Library + MSW with the hub fake extended by `labProgress`/`labFinished` pushes: start (body checked), disabled without a version, refusal, hub progress then finish, polling fallback, cancel, verdict gates and limits, report and links, report error, runs error, ledger with sweep row. Transport and hub unit tests cover the two new handlers.

## Open questions
None open (all taken as recommended above).

## Done when
- [x] Owner answers taken as recommended (standing instruction 2026-10-22), status Approved
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] No contract change, no migration
