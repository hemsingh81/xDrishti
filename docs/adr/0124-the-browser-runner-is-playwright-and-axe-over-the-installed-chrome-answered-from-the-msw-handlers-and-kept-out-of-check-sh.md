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

## Follow-up run 2026-10-26 (branch feature/p8-16-followup)
Full matrix on the same commit base, before the follow-up (24 screen rows, 4640 results: 2921 pass, 94 fail, 1625 na) and after it (34 screen rows, 6520 results: 4156 pass, 81 fail, 2283 na). Failures per check; "after" is split into the 24 rows that existed before and the 10 rows added now (Side, Calendar, MAE/MFE, Exits, Monte Carlo, Instruments, Sectors, Smart rules, Loss tags, Settings > Charge sheet):

| Check | Before (24 rows) | After, same 24 rows | After, 10 new rows |
|---|---|---|---|
| C3 touch targets | 8 | 4 | 0 |
| C5 axe (light) | 5 | 4 | 0 |
| C8 axe (dark) | 2 | 2 | 0 |
| C10 wide tables | 2 | 0 | 0 |
| C12 200 % text | 11 | 0 | 1 |
| C13 focus covered | 0 | 0 | 1 |
| C14 performance | 50 | 49 | 20 |
| C15 live regions | 16 | 0 | 0 |

C14 splits in two: layout shift above 0.2 went from 27 of 50 cells to 10 of 70 (all remaining are listed below); the interactive-ready time is still 4.1 to 4.6 s in 69 of 70 cells (the only cell under 4 s is Audit at 360: 3.7 s), which is the hard limit, so those cells still fail. The other C-numbers not in the table were 0 before and are 0 after. C4 did not fail in 77 repeated runs of the two screens that had it flaky (Reports Overview and Decisions at 1440, 8 workers); one run on the new MAE/MFE row failed on a `role=combobox` select, and the runner now re-reads the ring for up to 320 ms before calling a stop ringless (the border widens by a transition that can still be running in the frame the style is read): that stop is the only C4 cause seen, it is a measurement timing, not a missing ring. No product code was changed for C4.

Fixed because of the runner (follow-up):
- Layout shift at 360 on every screen (0.634 each): the top bar flipped between one and two rows as the accounts and bell controls loaded in; their placeholders were 64x30 and 36x36 against 58x44 and 44x44 controls. They now have the real phone size. CLS at 360 is 0.000 on 12 of the 25 rows that existed before (was 0.634 on 12).
- C15: the per-panel `LoadingState` is a status region with `aria-live="off"` (its words stay in the page); one `LoadingAnnouncer` in the app shell says "Loading…" once and "Loaded" once for the screen (it polls only while a first load runs). The sign-in check and the login page pass `announce`. Reports Overview went from 12 announcements to 3, Plans 9 to 4, Paper ledger 8 to 5 (the others in those two are the alert and the page's own status). The runner no longer counts a region with `aria-live="off"` (it is silent for a screen reader).
- C12: tables and heat grids, the bar list and the column bars were grid items that could not shrink, so one wide table widened its whole card (and the page on MAE/MFE: 2094 px); they now carry `min-width: 0`. The Key figures tiles take one column when enlarged text would make a tile narrower than 9 rem. The bar list's centre line was `width: 1`, which MUI reads as 100 %: a track-wide band that overflowed the card (a visible defect at every size; now 1 px). The heat grid's visually hidden cell names are positioned inside the grid's scroller. The runner skips the two false positives (the notch legend of an outlined field and 1 px visually hidden spans).
- C3/C5/C10: a clickable row of the shared grid is 44 px high on a phone (dense rows that open nothing stay as they are); an empty or failed grid region takes keyboard focus so it can be scrolled by keyboard; the runner counts a roving-tabindex row as keyboard reachable; the plan-list link in Grade is 44 px on a phone and 24 px otherwise.

Not reproduced at this commit: the C3 residuals named in the brief (native selects and inputs at 36 px at 360 on Decisions, Plan vs actual and Baskets; segmented toggles 36 px wide on Lessons, Experiments and Models; Audit row and alert-action buttons). The matrix shows C3 passing on those rows (24 to 33 targets at 44 px or more) in the full state at 360: nothing changed for them, and no component override was made.

Findings left (screen and check), after the follow-up:
- C14, time: interactive-ready 4.1 to 4.6 s under 4x CPU and Fast 4G in 69 of 70 cells. Reading the waterfall of Paper ledger at 1440 (resource timing): the entry chunk ends at 1.6 s, then about 50 small chunks of the app shell load until 3.1 s (bandwidth bound: they finish about 25 ms apart), the session request only starts after them because the router waits for the lazy shell before it renders the auth guard, then the page chunk, the tab chunk and the data calls follow one after the other (3.2, 3.6, 3.8 and 4.2 s). Of the shell fan-out about 70 KB gzip is the whole accounts feature (`accounts-*.js`, text fields, dialogs), pulled in because the `@/features/accounts` barrel statically exports `AccountsPage`, `RiskProfilesView` and `RoleMatrixView` next to the one hook the nav badge needs. Making those three lazy in the barrel would remove it from the first paint (est. 0.3 s in this lab), but it changes three public exports and their tests; not done here. A preload of the session request would not help (it is not on the critical path). No cheap safe change was found.
- C14, layout shift above 0.2 (cell: shift): Learning Setups 360 0.27; Reports Overview 360 0.41; What works 360 0.41; Instruments 360 0.37; MAE/MFE 360 0.41 and 1440 0.29; Cell detail 360 0.45; Journey 360 0.44 and 1440 0.23; Plans 1440 0.34. Cause: a loading placeholder panel is replaced by a content panel of a different height (Key figures loads about 190 px tall and ends about 300 px); each fix is a per-panel reserved height. Between 0.1 and 0.2: Experiments 360, What works 1440, Distribution 360, Instruments 1440, Side 360, Strategies 360, Cell detail 1440, Plans 360.
- C3/C5/C8 (Trade replay, 10 results): the TradingView attribution link (35 x 19, a licence duty of the chart library) and the chart container's `nested-interactive` (the library puts a link inside its interactive canvas); third-party markup.
- C12 and C13 (Settings > Charge sheet at 360): the scrollable tab strip of Settings overflows its root by 3 px (the scroll-button arrows) and the focused last tab sits under the right arrow; the strip is MUI `Tabs` with `allowScrollButtonsMobile`; not changed.
- C4: no failure left; the one flake seen is a runner timing and is handled in the runner.
- Fixture limits of the new rows: Exits and Monte Carlo are answered with empty lists (their populated builders live inside their test files), so the checks cover the screen, states and layout, not a filled comparison; Side, Calendar, MAE/MFE, Instruments and Sectors use the shared report fixtures. Cell detail and Trade replay already had rows (P8-11).
- `width: 1` (MUI: 100 %) is still in `TokenStatus.tsx` and `CategoryLegend.tsx`; not reported by the runner, not changed.
- Coverage limits: C7 met a chart canvas in only 8 cells (na elsewhere); C16 Back runs at 1440 only; not in the matrix: bell and drawer, Escape-closes-and-returns-focus.

## Consequences
- The honest limits of the spec stand: fixtures not owner data; Chrome only; a phone is emulated; no screen reader run.
- A screen added later adds one entry to `e2e/matrix.ts`.
- The owner walkthrough sheet of the spec (real stack, four widths, dark) is still to be done and recorded here: pass/fail and date: _not yet done_.
