# P8-16 — Responsive, accessibility and performance pass over every P8 screen

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P8 — Reports & UI, slice P8-16 of [P8-00](P8-00-reports-and-ui-plan.md) (360 px to 4K)
- **Design refs:** docs/design/13-reports-and-ui.md §1d (responsive bands), §1e (visual system), "Notifications and pop-ups" · P8-00 "Accessibility and responsive rules", acceptance criteria 3 and 8, risk 9, Q16 · frontend/CLAUDE.md (a11y, tests, bundle) · P7-17 honest limit ("looked at only through tests; not seen in a browser") · WCAG 2.2 AA
- **Needs:** P8-01 (kit, guard). Runs as the screens land: P8-02, 03, 04, 08, 09, 10, 11, 12, 13, 14, 15 each add their checklist rows to its Done; this slice builds the machinery and the global fixes first and makes the final pass. P8-05, 06, 07 have no screen (rows say so).
- **Size:** 3.5 agent-days (runner and fixtures 1.25, global fixes 0.75, first full pass 1, report and owner sheet 0.5). Fixes found per screen are each screen's own work, not this slice's (P8-00 listed 3; the added half day is the runner, see Findings in the index).
- **Safety:** no. Adds dev dependencies and a manual script; no runtime write, no network exposure (the preview server binds 127.0.0.1), no secret (API answers are fixtures, no login). **Not covered:** installing a browser binary is a download and is the owner's act (Decisions 2).

## Goal
"Responsive" and "accessible" stop being claims proved only by jsdom: every P8 screen is opened in a real browser at 360, 768, 1440 and 2560 px, in light and dark, in each of its states, checked against one written checklist and axe, with the result and screenshots kept; the layout rules of doc 13 §1d that the theme never implemented are implemented; the owner gets a short walkthrough sheet for the end of the phase.

## Scope
**In:**
- **Runner** (manual, like `test:clock`): `frontend/e2e/` with `@playwright/test` and `@axe-core/playwright`, `playwright.config.ts`, `npm run test:browser`, a state-driven API layer built from the existing MSW handlers (Decisions 3), a results recorder (`scripts/p8-evidence.mjs`), evidence under `data/p8-evidence/` (git-ignored).
- **Global fixes**: skip link and `main` focus target in `AppShell`; the large-screen root font of doc 13 §1d (not in the theme today: `rootFontSize` 14 is fixed); 44 px touch targets below 480 px; a global `prefers-reduced-motion` check; heatmap, band and candle token pairs added to `contrast.test.ts`; `QueryState` live regions; the filter bar collapse and table-in-card rules where a screen lacks them.
- **The checklist** below, run for every screen row; the result file; the owner walkthrough sheet.
- **Performance measurement**: interaction-ready time and layout shift per screen under 4x CPU throttle (reported).
**Out:** fixing a screen's own defects beyond the global rules (each screen slice reopens), a visual-regression service, Storybook, running against the real stack or real data (owner walkthrough), a physical phone (the stack is 127.0.0.1 only until P8-21's access decision), screen-reader testing on real assistive software (listed as a manual item for the owner, not automated), PWA behaviour (P8-21), any change to API or data.

## Acceptance criteria
1. Given `npm run test:browser`, then for each screen row of the matrix and each width in {360, 768, 1440, 2560}, each colour scheme in {light, dark} (dark at 1440 only for the whole matrix, both at 360) and each state in {loading, error, empty, thin, full, plus the screen's `not_evaluable`/`unavailable` fixture}, the page loads from the built app with API answers from fixtures, no request is unanswered, and results are written as one JSON line per check to `data/p8-evidence/browser-checklist.jsonl` with screen id, width, scheme, state, check id, `pass|fail|na`, a note, the commit and the date. *Runner self-test `e2e/runner.spec.ts` (two screens).*
2. Given any screen at any width, then `document.scrollingElement.scrollWidth <= clientWidth` (check C1); a wide table or heatmap scrolls inside its own labelled region (C10). Failing rows are listed with the offending element's selector.
3. Given widths below 480 px, then every interactive element that is not inline text in a sentence measures at least 44 x 44 CSS px (C3), and at all widths at least 24 x 24 (WCAG 2.2 AA 2.5.8, axe `target-size`); the global rule is in `globalStyles.ts` for `.MuiButton-root, .MuiIconButton-root, .MuiTab-root, .MuiChip-clickable, [role=button]` under a 479.95 px query.
4. Given widths of 1680 px and above, then the root font is 15 px (1680 to 2559) and 16 px (2560 and above) via media queries in `globalStyles.ts` (from the token breakpoints `pagePadding.wide` / `.ultra`), every rem-based size follows, and the checklist C2/C10 pass at 1680 and 2560; a unit test pins the two media rules.
5. Given axe-core with tags `wcag2a, wcag2aa, wcag21a, wcag21aa, wcag22aa` on each screen state, then there are zero violations (C5); `best-practice` findings are recorded as notes, not failures. Colour contrast inside canvas charts is outside axe's reach: charts are covered by the token tests (criterion 7) and the table fallback (C7).
6. Given the keyboard only, then Tab order follows the visual order, a focused element is never hidden under the sticky top bar (WCAG 2.4.11, scroll margin), the 2 px focus ring is visible (computed outline width >= 2 px) on every stop, Escape closes dialogs/drawers and returns focus, and the skip link is the first stop and moves focus to `main` (C4, C13). The runner Tabs through at most 80 stops per screen and fails on a trap or a stop with no visible ring.
7. Given the new tokens (heatmap ramp stops, masked/hatched cell, candle up/down/hollow, price-line colours, band colours of the Journey), then text on them is at least 4.5:1 and chart marks at least 3:1 against the surface in both modes, asserted in `contrast.test.ts`; a positive control proves the check can fail.
8. Given `prefers-reduced-motion: reduce`, then after load `document.getAnimations()` is empty on every screen (C9) and no count-up, slide or auto-scroll runs.
9. Given a state change (loading to loaded, a filter change, an error), then exactly one polite live-region announcement is made (role `status`, or `alert` for an error), not one per cell (C15); checked by reading the live regions' text history in the runner.
10. Given any chart, then an adjacent or toggleable table holds the same numbers and a text summary exists (C7); a heatmap cell is a button named "strategy, timeframe, side, exit: expectancy, n, state".
11. Given colour removed (`filter: grayscale(1)`) on every state fixture, then each state is still readable as an icon plus a word and the runner finds the state word on the page (C6).
12. Given reload and Back, then tab, filters and the open drill-down return from the URL (C16).
13. Given the performance run (4x CPU throttle, Fast 4G profile via CDP, built app), then per screen the time from navigation to its answer element (the screen's `data-ready` marker, added by this slice to each route's main region) and the layout shift (CLS) are recorded; the manual script fails only above 2x the budget (budget: interaction-ready 2 s, CLS 0.1); the figures are reported, not part of `scripts/check.sh`.
14. Given `scripts/check.sh`, then it is unchanged and green with the new dev dependencies and the `e2e/` folder (type-checked and linted through its own tsconfig; no browser needed); `npm run size` unchanged within 0.3 KB gzip (the media rules and the skip link are the only eager additions, measured).
15. Given the end of the phase, then the report `data/p8-evidence/browser-checklist-summary.md` lists per screen and width the failed checks, and the owner walkthrough sheet (below) is in the ADR.

## The checklist
**Common checks** (run on every row; ids are the keys in the result file): C1 no horizontal page scroll · C2 the answer above the fold at 360 x 800 (review-type screens: Learning overview, What works, `/plans`) · C3 touch targets · C4 keyboard order, focus ring, no trap · C5 axe · C6 state without colour · C7 chart has a table and a summary · C8 dark mode axe (1440 and 360) · C9 reduced motion · C10 wide tables/heatmaps scroll inside their card, sticky header kept · C11 loading, error, empty fixtures render the kit's states (not a blank) · C12 reflow: at 360 px with 200% text zoom nothing is cut off or overlaps (WCAG 1.4.4/1.4.10) · C13 focus not obscured · C14 performance figures recorded · C15 live regions · C16 URL state and Back.

**Per-screen items** (S-ids; each row also runs C1 to C16; "route" is where it is opened; fixtures are the screen's own MSW states):

| Id | Screen and route | Screen-specific items |
|---|---|---|
| P8-02 | Learning frame, Overview, Setups: `/learning`, `?tab=setups` | S1 tabs scroll sideways inside their strip on phones, the selected tab stays visible; S2 headline tiles 2-up at 360, 4 across at 2560; S3 heatmap cell is a button with the full accessible name, hatched not-enough cells carry the word; S4 cell drawer is full-screen below 480 and returns focus to its cell; S5 lifecycle funnel stacks vertically at 360; S6 empty store shows "no snapshot yet" with the verb, no digit |
| P8-03 | Lessons, Experiments, Hypotheses, Models: `?tab=lessons|experiments|models|digest` | S1 lesson cards single column below 900; S2 experiment table scrolls in its card; S3 fold and calibration tables keep headers; S4 "no model stored" is the first view, not an error; S5 outcome words (Better, NotBetter, Inconclusive, CheaperAssumption) are text; S6 drawers: focus in, Escape out |
| P8-04 | Plan vs actual, Decisions, Autonomy (read), phase checks (tabs `plan`, `decisions`, `autonomy`, `checks` per P8-04, aligned in the consistency review) | S1 waterfall has its table; steps without data say `not_evaluable`; S2 Autonomy tab contains no control that writes (assert no non-GET and no password field); S3 phase-check lines use `VerdictChip` words; S4 links to `/plans` work |
| P8-05 | Report engine core | no screen (pure); covered through P8-08 rows |
| P8-06 | Report endpoints | no screen; p95 evidence is in its ADR |
| P8-07 | Basket benchmarks | no screen of its own; the buy-and-hold column is checked in P8-09 |
| P8-08 | Reports frame, Overview, What works: `/reports`, `?view=what-works` | S1 catalogue is a select/drawer below `md`, a left list from `md`; S2 filter bar collapses to one "Filters (n)" control below `md` and active filters stay as chips; S3 Data used panel readable and its run picker keyboard-operable; S4 KPI tiles 2-up at 360; S5 the What works headline (or "Nothing has enough evidence yet") is above the fold at 360 x 800; S6 ranked and unranked lists keep n and the reason as text; S7 URL round trip including `baskets=` |
| P8-09 | Basket comparison, Pivot, lenses: `?view=baskets|pivot`, `lens=` | S1 basket picker usable at 360 (2 to 6 selected, chips); S2 overlaid curves have a table; S3 pivot scrolls inside its card with sticky row/column headers; masked cells carry the word and n; S4 non-additive note visible; S5 lens selector keyboard-operable |
| P8-10 | Catalogue reports: ids `strategies, side, calendar, maemfe, distribution, instruments, sectors, exits, montecarlo` | S1 each chart has its table; S2 scatter shows its cap/downsample sentence; S3 histogram bins readable at 360; S4 calendar heat grid does not overflow the page; S5 pointer entries (regime, calibration, selector) are text with reasons |
| P8-11 | Drill-down and replay: `/reports/cell`, `/reports/trades/:runKey/:ordinal` | S1 chart height at least 260 px at 360 and not above 70 vh at 2560; S2 timeframe tabs and Previous/Next event buttons keyboard-operable, live region speaks the event; S3 event table and "Bars (n)" table present; S4 coherence banner and attribution link visible; S5 landscape phone 812 x 375 still shows chart and events; S6 trade list Grid rows keyboard-navigable at 360 |
| P8-12 | Journey: `/learning?tab=journey` | S1 headline sentence above the fold; S2 window chart has its table; S3 the lesson-band timeline collapses to a list at 360; S4 pins readable; S5 the live line states `not_evaluable` as text |
| P8-13 | Smart rules, Loss tags: `?tab=smart-rules`, `?tab=loss-tags` | S1 rule cards stack below 900; S2 tag bars plus table; S3 `not_evaluable` outcome lines show their command text, selectable; S4 worst-loss list wraps without horizontal scroll |
| P8-14 | Plan review additions: `/plans`, `?plan=` | S1 flags chips wrap, the banner collapses after 2 lines with "Show all" and never pushes the action area below the fold at 360; S2 per-account standing lines readable, "Show all checks" disclosure; S3 failed-closed banner above the list; S4 ticket cards 1 column below 900, 2 from 1680, up to 4 at 2560 (doc 13 §1d; current grid **UNVERIFIED**); S5 chart collapsed by default below `md`; S6 the no-order sentence is on every action area |
| P8-15 | Bell and drawer (any route) | S1 drawer is full width on phones; S2 item buttons at least 44 px; S3 severity is word plus icon; S4 any pop-up/toast does not cover the action area and honours reduced motion; S5 the five built plan items (awaiting review, nightly failed, tickets lapsed, armed/invalidated, fills not matched; `PlanNotice { id, kind, severity, text, route, planId?, at? }`, ADR 0101) read as word plus icon with an Open route; ticket-alert items of P7-20 are checked only if P7-20 is built and enabled |
| P8-18 to 22 | Owner-gated slices | each adds its rows when (and only if) built; none is in this pass |

**Evidence kept:** `data/p8-evidence/browser-checklist.jsonl` (one line per check), `data/p8-evidence/shots/<screen>-<width>-<scheme>-<state>.png` (full page), `browser-checklist-summary.md`. `scripts/p8-phase-check.sh` (P8-17) reads them.

**Owner walkthrough sheet (end of phase, real stack, desktop browser device emulation at 360, 768, 1440 and 2560 px, then dark mode):** for each of Learning overview, Setups, Reports Overview, What works, a drill-down to one trade replay, Plan review, the bell: (1) can you find the answer in the first screen at 360; (2) can you do it with the keyboard only; (3) is every number next to its n and basis; (4) is any state shown only by colour; (5) anything cut off. Write pass/fail and the date in the ADR. A real phone is not possible until P8-21's access decision.

## Contract
- **API / Data:** none. **UI:** global changes only (`AppShell` skip link, `globalStyles.ts` media rules, `tokens.ts` ramp/band tokens, `data-ready` markers); no new route.
- **Tooling (pinned exactly, devDependencies, `save-exact`):** `@playwright/test` **1.63.0**, `@axe-core/playwright` **4.13.0** (depends on `axe-core ~4.13.0`). Files: `frontend/playwright.config.ts`, `frontend/e2e/{runner.spec.ts, matrix.ts, api.ts, checks/*.ts}`, `frontend/tsconfig.e2e.json`, `frontend/scripts/p8-evidence.mjs`. `npm run test:browser` (all), `npm run test:browser -- --grep P8-08` (one), `npm run test:browser:perf`. Excluded from `vitest` (include only `src/**`) and from `scripts/check.sh`.
- **Server for the runner:** `vite preview` on **127.0.0.1:4173** of the production build (never `0.0.0.0`); `page.route('**/api/**')` answers from fixtures; `**/hubs/**` is aborted (the screens already degrade without the hub).
- **Events/jobs:** none.

## Non-functional
A full matrix run takes minutes (about 11 screens x 4 widths x states) and is manual; `--grep` runs one screen. Memory and CPU of the runner are irrelevant to the app. Security: the runner reads no secret and sends nothing out of the machine; screenshots hold fixture data only. Added dev packages are not shipped (`npm run size` proves the bundle).

## Test plan
- **Unit (Vitest):** the two media rules; contrast pairs for the new tokens (both modes) with a positive control; `skip link` renders first and focuses `main`; `data-ready` appears when a route's main query settles; recorder merges and de-duplicates lines.
- **Runner self-test:** two screens x two widths prove the machinery, including that a planted overflow (a 2000 px div) fails C1 and a planted 20 px button fails C3.
- **The pass itself** is the evidence for criteria 2 to 13; failures become the screen slices' defects or are fixed in the global files here.
- **Real-data check:** none (the owner walkthrough is the real-stack look).

## Decisions (researched)
1. **Q16: Playwright plus axe as a manual script, not in `scripts/check.sh`.** Versions read from npm 2026-10-24: `@playwright/test` latest 1.64.1 (2026-10-14) and 1.63.0 (2026-09-04); `@axe-core/playwright` 4.14.0 (2026-10-15) and 4.13.0 (2026-08-11); `axe-core` 4.14.0 (2026-10-05). Pinned **1.63.0** and **4.13.0**: the newest releases are under three weeks old and the earlier ones have had weeks of use; Playwright is Apache-2.0, axe-core and its wrapper MPL-2.0 (dev only, not shipped, no change to the app's licence duties). Sources: https://www.npmjs.com/package/@playwright/test, https://www.npmjs.com/package/@axe-core/playwright (versions and dates by `npm view`). Rejected: none at all (jsdom cannot assert 360 px or 4K, P8-00 risk 9); Storybook/Chromatic or any hosted visual service (screens and data leave the machine); Lighthouse CI (its a11y audit is axe underneath, and its lab performance numbers do not cover our states); putting the browser run in `check.sh` (minutes, a binary the check must not depend on, flaky timing).
2. **Browser binary.** `/Applications/Google Chrome.app` exists on the owner's machine (checked 2026-10-24); the config uses `channel: 'chrome'`, so **nothing is downloaded**. If Chrome is absent, `npx playwright install chromium` is a download of about 150 MB (**UNVERIFIED size**) and is the owner's explicit act; the slice never runs it. Whether Playwright 1.63 drives the installed Chrome's current version is **UNVERIFIED** (first runner step; fallback: the owner runs the one install). In an agent session that has the built-in browser pane (`resize_window` to custom sizes, `read_page`, screenshots), the agent may use it for exploratory looks and records the same check ids by hand in the jsonl; the scripted run stays the repeatable evidence.
3. **API answers from the existing MSW handlers, no login, no service worker.** The repo's `src/test/server.ts` holds the fixtures for every endpoint; MSW 3.0.2 exports `getResponse(handlers, request)` (read in `node_modules/msw/lib`, 2026-10-24), so `page.route` can ask the same handlers for a response and fulfil it. No `mockServiceWorker.js` enters `public/` (which would also collide with P8-21's service worker), no password is needed, and states (error, empty, thin) are made by prepending override handlers exactly as tests do. Whether relative-path handlers match Playwright's absolute URLs in Node is **UNVERIFIED**; first runner step; fallback: rewrite the request URL onto `http://localhost` before calling `getResponse`. Rejected: running against the real stack (needs the owner's password, real data changes every run, not deterministic).
4. **Large screens: implement doc 13's rule.** Doc 13 §1d says the base font grows from 1680 px; the theme fixes 14 px (`rootFontSize`) and only the page padding grows (`tokens.ts`). The global `html` font size gets two media rules (15 px, 16 px). Alternative rejected: leaving a documented rule unimplemented, or scaling by `zoom` (not standard).
5. **Touch targets: 44 below 480, 24 everywhere.** WCAG 2.2 AA requires 24 x 24 CSS px (2.5.8); doc 13 asks 44 on phones (the MUI `large` control is 44, `controlHeight.large`). Both are checked; the 44 is a global CSS rule so no screen needs its own.
6. **Skip link and `main` target.** `AppShell` already has `component="main"`; no skip link exists (searched). Added: one link, first in tab order, hidden until focused.
7. **Dark mode at 1440 for the matrix, both modes at 360.** Contrast failures are token problems, not width problems; this keeps the run affordable. The token tests cover both modes everywhere.
8. **Performance as a reported figure, asserted at 2x.** A lab run on a developer Mac under CPU throttle is a trend, not a promise about a mid phone; the 2 s goal of P8-00 is measured and written down, and only a breach of twice the budget fails the manual script. `data-ready` is a stable marker per route instead of guessing from timers.
9. **Evidence outside git.** `data/` is git-ignored; screenshots of fixtures are not secrets but are bulky; the summary is copied into the ADR by hand.

## Risks and honest limits
- Fixture data, not the owner's data: layouts are proved, real-data surprises (long symbol lists, 214-instrument tables) are not; the owner walkthrough on the real stack is the second look.
- axe finds roughly the mechanical share of WCAG failures; it cannot judge reading order sense, alt text quality, or canvas contrast. The keyboard and grayscale checks and the owner sheet cover part of the rest; no screen reader was run by an agent.
- Chrome only (no Safari/Firefox); the installed Chrome is newer than Playwright's bundled one.
- A phone is emulated, not real; touch and the on-screen keyboard are not exercised.
- Timing under throttle varies run to run; trends only.

## Open questions
None blocking. For the owner (an owner act, default until then): if Chrome is not usable by Playwright, run `npx playwright install chromium` once (a download); until then the pass uses the built-in browser pane where available and the unit tests, and the checklist file marks every unchecked row `na`.

## Done when
- [ ] All acceptance criteria have evidence (`browser-checklist.jsonl` and summary kept; owner walkthrough recorded in the ADR)
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] No contract change; no migration
- [ ] ADR (next free ADR number at build time: browser evidence, large-screen font, touch targets); doc 13 §1d "As built" note; `frontend/CLAUDE.md` gains the `test:browser` line; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- P8-15 row rewritten for the built slice (ADR 0101): no absorbed P7-20 source, `plan_fills_unmatched` exists. P8-02 row: heatmap is the built `Heatmap`/`HeatTile` (P8-02b extracts `HeatGrid`).

## Build note 2026-10-26 (partial, global fixes only; NOT verified in a browser)
Global rules in place, pinned by CSS-text/jsdom tests only: criteria 3, 4 and 6 are partial (44 px phone rule with doubled
specificity and dense exemptions, root-font media rules, skip link and focusable `main`, scroll padding, `ButtonTabs` sideways
scroll moving only the strip; the tab focus ring is inset so the strip does not clip it). Whether the cascade, exemptions and
clipping behave in Chrome is unverified. Criterion 14: `npm run size` 196.5 KB of 200 KB (3.5 KB spare). Not built: the
Playwright and axe runner (owner decision on dev dependencies; criteria 1, 2, 5, 8 to 13, 15), `data-ready` markers, evidence
files, owner sheet. No ADR needed.

## Build note 2026-10-26 (runner built and run, ADR 0124)
Runner built: `playwright.config.ts`, `e2e/` (matrix, api, run, checks, self-test), `tsconfig.e2e.json`, `scripts/p8Checklist.mjs`, `data-ready` on
`main`, `LoadingState` text in its live region; `npm run test:browser`, `test:browser:perf`. Playwright 1.63 drives the installed Chrome.
First full run over 22 screen rows: see the ADR for counts and the unfixed findings. Criteria 1, 2, 5, 8 to 13 have evidence for the
fixtures run; criterion 6 (Escape closes and returns focus) and the drill-down, bell, Smart rules and Loss tags rows are not in the matrix;
criterion 15's owner walkthrough is still to do. Results file name is `browser.jsonl` (field `result`), not the name written above.

## Build note 2026-10-26 (follow-up, ADR 0124 "Follow-up run")
Open runner findings worked through; full matrix re-run (34 screen rows, 6520 results: 4156 pass, 81 fail, 2283 na; before 94 fail over 24 rows). Cleared: C10, C15, and C12 on the 24 old rows (C12 11 to 0); C3 8 to 4 and C5 5 to 4 (the rest is the TradingView link and chart markup on the replay); layout shift at 360 on the shell is gone (0.634 to 0.000 on 12 rows). New rows: Side, Calendar, MAE/MFE, Exits, Monte Carlo, Instruments, Sectors, Smart rules, Loss tags, Settings > Charge sheet (Exits and Monte Carlo with empty-list fixtures). Still failing: C14 interactive-ready 4.1 to 4.6 s in 69 of 70 cells and layout shift above 0.2 in 10 cells (list in the ADR), C12/C13 on the Settings tab strip at 360, C3/C5/C8 on the replay. The C3 residuals named for selects, segmented toggles and Audit buttons did not reproduce. Criterion 14: `npm run size` unchanged in kind (the announcer is in the lazy shell chunk). No acceptance criterion is claimed beyond what the runner gave; the owner walkthrough is still to do.
