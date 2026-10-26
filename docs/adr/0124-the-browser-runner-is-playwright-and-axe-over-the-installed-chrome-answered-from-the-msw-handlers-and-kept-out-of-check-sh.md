# 0124 — The browser runner is Playwright and axe over the installed Chrome, answered from the MSW handlers, and kept out of check.sh

- **Status:** Proposed (spec [P8-16](../specs/P8-16-responsive-accessibility-and-performance-pass.md), runner built 2026-10-26; the spec is still Draft). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-26.
- **Relation to other ADRs:** closes spec Q16 (owner decision 2026-10-26: Playwright plus axe); the global rules of the first half of P8-16 (44 px phone targets, root font from 1680 px, skip link) were built earlier without a browser and are now measured.

## Decision
1. **Playwright 1.63.0 and @axe-core/playwright 4.13.0**, exact, dev only, as the spec pinned them. The browser is the installed Google Chrome (`channel: 'chrome'`); nothing is downloaded and `playwright install` is never run. Playwright 1.63 drives the installed Chrome without trouble (checked 2026-10-26).
2. **Server and API.** `vite preview` of the production build on 127.0.0.1:4173 (`--strictPort`, never 0.0.0.0). `page.route(/\/api\//)` answers from the repository's own MSW handlers through `getResponse` (`e2e/api.ts`): the test server's handlers plus each screen's `mount` fixtures from `src/test`, no service worker, no login, no secret, nothing leaves the machine. The relative handler paths need a `location` in Node: `e2e/shim.ts` defines one (the spec's fallback, small). `/hubs/` is aborted. A request with no handler is answered 404 and recorded as check C0 (a finding). A non-GET is recorded (C0-writes). `loading` holds the data calls, `error` answers 500.
3. **The matrix** (`e2e/matrix.ts`): the built P8 screens (Learning overview, Setups, Lessons, Experiments, Models, Digest, Plan vs actual, Decisions, Autonomy, Phase checks, Journey; Reports overview, What works, Baskets, Pivot, Strategies, Distribution; Plans; Audit; Paper ledger, Review, Compare, Phase) at 360, 768, 1440, 2560, light in six states, dark (full) at 360 and 1440. A state with no fixture is recorded `na`. Not covered: Smart rules and Loss tags (no fixtures yet), the catalogue reports other than two, the drill-down and replay (P8-11), the bell and drawer, any pop-up, and `/reports/cell`.
4. **Checks** (`e2e/checks/*`): C0 requests answered, C1 page scroll with the offending selector, C2 above the fold at 360 x 800 (proxy: the first content block starts above 800), C3 touch targets (44 below 480, 24 always), C4 keyboard (skip link first, 2 px ring, no trap, at most 80 stops; a text field's 2 px border counts as its ring), C5 and C8 axe (WCAG tags fail, best-practice only noted), C6 state word with colour removed, C7 chart has table and summary, C9 reduced motion (the whole matrix runs with it on), C10 wide tables scroll in a labelled region, C11 kit states, C12 200 % text at 360 (page scroll and clipped text; overlap is not measured), C13 focus not covered, C14 performance, C15 live regions (announcements of `status`/`alert` regions, de-duplicated; fails above 6 or on a missing alert), C16 URL state (reload, then Back from another route). C14: 4x CPU and Fast 4G through CDP, time to `main[data-ready]` and layout shift; it fails only above twice the budget (4 s, 0.2).
5. **Results.** One JSON line per check in `data/p8-evidence/browser.jsonl` with `screen, width, scheme, state, check, result, note, commit, date` (git-ignored); `scripts/p8Checklist.mjs` (also `node scripts/p8-evidence.mjs checklist`; warns when rows of more than one commit are present, and a full run starts a fresh file) keeps the newest line per key and writes `browser-checklist-summary.md`; screenshots in `shots/`. The self-test writes `runner-selftest.jsonl`. `npm run test:browser` (`scripts/test-browser.mjs`: Playwright, then the recorder, exit status the worse of the two) is manual and is not in `scripts/check.sh`; `e2e/` is type-checked (`tsconfig.e2e.json`, a reference of `tsconfig.json`) and linted by `npm run check`; Vitest includes only `src/**`.
6. **`data-ready`.** `ReadyMarker` (a child of `AppShell` keyed by the route) renders an empty `data-ready` element once no query is fetching and no loading status (`[role=status] > [role=progressbar]`) is on the page, after 150 ms; it subscribes to the query cache itself, so the shell never re-renders for it, and it starts over on every navigation. `LoadingState` now has its text inside its polite status region (a spinner alone said nothing). These add a few bytes to a lazy chunk and the shared UI.

## Runner self-test
`e2e/runner.spec.ts`: two screens at 360 and 1440 report every check with no unanswered request, a planted 2000 px div fails C1 and names the element, a planted 20 px button fails C3 at 360 (44) and at 1440 (24). Six of six pass.

## Results
First run (commit cf9d8b6c plus the first fixes, 22 screen rows): 191 fail. After the second round of fixes and the P8-11 rows (24 screen rows, 4640 results: 2922 pass, 93 fail, 1625 na), failures per check, before then after:

| Check | Before | After |
|---|---|---|
| C1 page scroll | 4 | 0 |
| C3 touch targets | 41 | 8 |
| C4 focus ring | 24 | 0 |
| C5 axe (light) | 24 | 5 |
| C6 state word | 8 | 0 |
| C8 axe (dark) | 6 | 2 |
| C10 wide tables | 0 | 2 |
| C11 kit states | 4 | 0 |
| C12 200 % text | 23 | 10 |
| C13 focus covered | 1 | 0 |
| C14 performance | 44 | 50 |
| C15 live regions | 12 | 16 |

The after column includes the two new P8-11 screens (cell detail, trade replay), which account for most of C10, C5, C8 and part of C3 and C15. Fixed because the runner found them: the hidden skip link was 100 % wide (`width: 1` is 100 % in MUI); the sidebar `ul` held links; top-bar, alert-action, chart-toggle and every other small button now 44 px on phones; single-line fields 44 px high on phones; segmented toggles 44 px; the top bar wraps at 200 % text; chips wrap instead of widening their row; the Pivot builder and plan list no longer widen the page; Journey "Period and knobs" was a `dl` without terms; heat-tile caption and the disabled filter reasons were under 4.5:1; `main` is a keyboard-reachable scroll region (tabindex 0); grids and the replay tables are labelled scroll regions; the Audit loading state has a status region; links in term lists have a 24 px (phone 44 px) target. Some C4 and C13 failures were the runner reading a frame too early after Tab (the field border and ring are drawn a frame later); it now waits for it.

## Findings left (screen and check)
- C14 (all rows at 360 and 1440): interactive-ready 4.2 to 4.7 s under 4x CPU and Fast 4G, above the 4 s hard limit, and layout shift above 0.2 on Reports Overview and What works at 360. The 150 ms settle is part of the figure. A lab trend, not a promise about a phone. Not fixed: no cheap safe change.
- C15: Reports Overview (12 lines), Plans, Paper ledger and the cell detail (7) announce a status per loading stage on the way to loaded (session, lazy page, queries); fewer would need one shared loading owner per route.
- C12: at 200 % text a wide panel body is clipped by its card on Plan vs actual checks, Reports Overview, What works, Baskets, Pivot and Distribution at 360.
- C3: the TradingView attribution link on the replay (35 x 19, a licence duty), grid rows 39 px high on a phone (cell detail), one plan-list link; C5/C10: the cell detail grid's scroll region has no tabbable row in the fixture; the replay chart container (`nested-interactive`).
- Coverage limits: C7 met a chart canvas in only 8 cells (na elsewhere); C16 Back runs at 1440 only (no sidebar on phones); not in the matrix: Smart rules, Loss tags, bell and drawer, Escape-closes-and-returns-focus.

## Consequences
- The honest limits of the spec stand: fixtures not owner data; Chrome only; a phone is emulated; no screen reader run.
- A screen added later adds one entry to `e2e/matrix.ts`.
- The owner walkthrough sheet of the spec (real stack, four widths, dark) is still to be done and recorded here: pass/fail and date: _not yet done_.
