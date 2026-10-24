# P8-01 — Shared analytics kit, six honest states, bundle guard

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question of [P8-00](P8-00-reports-and-ui-plan.md) taken as recommended; none of them is a SAFETY item for this slice, see assumption 1). Built 2026-10-24.
- **Phase / roadmap row:** P8 — Reports & UI, slice P8-01 of [P8-00](P8-00-reports-and-ui-plan.md), 3 d (frontend and docs only)
- **Design refs:** docs/design/13-reports-and-ui.md §1c, §1d, §1e · P8-00 D3 (six states), D1 (evidence block), D7 (bundle discipline), acceptance criteria 2, 3 and 7 · frontend/CLAUDE.md (bundle, a11y, tests) · ADR 0098

## Assumptions (taken as recommended)
1. **Safety.** The slice adds no control that writes, starts a job, exports, installs, caches or notifies, and no setting. P8-00 Q1 to Q8 are all answered as recommended (read-only, no write actions in the first build); nothing here depends on them. The kit components only render what they are given.
2. **No backend change, no new dependency.** `package.json` gains one script (`size`); `package-lock.json` is untouched. The kit uses MUI, the icons package and `Intl` that the app already has.
3. **The server's payload shape is not known yet** (P8-06 defines it). The kit therefore defines the *UI* type `MetricState` and a null-safe constructor (`metricOf`); each screen slice maps its response onto it. No composite "health score" exists (P8-00 D10, Q12).
4. **"Six honest states".** The owner's wording lists value, n/a, not evaluable, loading, error and partial/provisional. P8-00 D3 lists value, insufficient, unavailable, not_evaluable, empty. The kit covers all of them in one type of eight kinds (table below); where the two vocabularies meet, the words printed are the ones in the table. Only `value` and `provisional` carry a number.
5. **Not built here, deferred to the slice that first needs it** (so nothing unused enters the repo): the `useUrlState` hook (P8-02, with `?tab=`), the heatmap / scatter / bar-with-error / histogram chart types and their `CategoryTable`-style fallbacks (P8-02 heatmap, P8-10 scatter and histogram, in the lazy ECharts chunk only), and the *screens* behind the Learning and Reports nav entries (P8-02, P8-08). The nav entries keep the "coming in phase" page; only their stale phase labels are fixed (Learning P6 to P8, Audit log P1 to P8, as P8-00 Q18 asks). The Suggestions entry (P9) is left as it is: P8-00 Q18 does not say what it becomes, and the inbox lives in `/plans`.
6. **KB is 1000 bytes** in the guard (the stricter reading of "200 KB"): 200,000 bytes gzip. The 195.3 KB measured by P8-00 with `gzip -c` reads 195.2 KB with Node's zlib at the same level (rounding of the per-file sizes).
7. **The kit is imported by path** (`@/shared/analytics`), not added to the `shared/ui` barrel: that barrel is part of the entry chunk and the kit is used only by lazy screens (the barrel's own comment states the rule for heavier widgets).

## Goal
Every number a report or Learning screen shows is drawn through one small kit that cannot show a non-value as a digit or as a green "ok", always sits beside its evidence (n, range, basis, provisional, limits), and a build that pushes the first-load JavaScript over 200 KB gzip fails `scripts/check.sh` the day it happens.

## Scope
**In:** `frontend/src/shared/analytics/` (`MetricState`, `NonValue`, `MetricValue`, `MetricCell`, `MetricTile`, `DataTable`, `EvidenceBlock`, `VerdictChip`, report formatters, public `index.ts`); the bundle guard (`frontend/scripts/firstLoad.mjs`, `size.mjs`, `npm run size`, a step in `scripts/check.sh`); Vitest tests for every state of every component and for the guard on a fixture build; the two nav-label fixes; this spec, ADR 0098, the roadmap row and a line in doc 13.
**Out (not in this slice):** any screen; a limit on the always-needed total (only printed); a hook or a chart type nothing uses yet (assumption 5); per-route chunk budgets (the guard prints the five largest lazy chunks, it does not fail on them; budgets are set when a route first needs one, P8-00 D7); Storybook; a Playwright or browser run (P8-16).

## The kit
| Kind | Carries a number | Printed (icon + word, always) | Colour |
|---|---|---|---|
| `value` | yes | the figure, formatted by the caller's `format` | neutral; `tone="gain-loss"` makes positive `success`, negative `error` (the sign is text) and **only** for this kind |
| `provisional` | yes | the figure, then "Provisional" and the reason | warning |
| `insufficient` | no | "Insufficient data" and "n 12 of at least 30" | secondary text |
| `unavailable` | no | "Not available" and the reason | secondary text |
| `not_evaluable` | no | "Not evaluable" and the reason | secondary text |
| `empty` | no | "No records yet" (and a reason if any) | secondary text |
| `loading` | no | "Loading" | secondary text |
| `error` | no | "Could not load" and the reason | error |

Rules the code enforces (not conventions): the `MetricState` type has no `value` field on a non-value, so a screen cannot pass one; a `value` or `provisional` whose number is `NaN` or infinite is drawn as `unavailable` (`resolveMetric`), never as "NaN" or 0; a stray `value` on a non-value coming from an API is ignored; the icon is `aria-hidden` and the word is the status, so nothing relies on colour; no non-value kind is ever drawn in the `good` colour (tests walk every element's computed colour in both modes, with a positive control proving the check can see it).

### Usage
```tsx
import { DataTable, EvidenceBlock, MetricTile, VerdictChip, metricOf, formatR, formatRate } from '@/shared/analytics';

// A tile: the figure in one of its states, the evidence as one line under it.
<MetricTile label="Expectancy" state={metricOf(row.expectancy, 'No trades in this window')} format={formatR}
  tone="gain-loss" evidence={{ n: row.n, range: run.range, basis: run.costed ? 'net' : 'gross' }} />

// A table: columns say how each cell is read; a missing figure prints its word, never "0".
<DataTable label="What works" rows={cells} rowKey={(c) => c.key} status={query.isPending ? { kind: 'loading' } : undefined}
  columns={[
    { kind: 'text', id: 'cell', header: 'Cell', render: (c) => c.name },
    { kind: 'metric', id: 'exp', header: 'Expectancy', metric: (c) => c.expectancy, format: formatR, tone: 'gain-loss', n: (c) => c.n },
    { kind: 'metric', id: 'win', header: 'Win rate', metric: (c) => c.winRate, format: formatRate },
  ]}
  evidence={{ n: total, range, basis: 'gross', population: runKeys, limits: report.limits }} />

// A phase-check line.
<VerdictChip verdict="not_evaluable" />   // MET / NOT MET / NOT EVALUABLE, icon and words
```
`EvidenceBlock` requires `n`, `range` and `basis` (each may be `null` / `'unknown'`, which prints "n not reported", "range not stated", "Basis not stated"): a report that omits its evidence does not type-check. `population` ("Data used") and `limits` are the server's strings, rendered as text (never as markup). `DataTable` scrolls inside its own labelled, keyboard-focusable region, keeps a real header and row headers, and replaces an empty body by "No records yet"; `status` swaps the body for "Loading" (the table is `aria-busy`) or "Could not load". Formatters (`formatCount`, `formatRatio`, `formatSigned`, `formatR`, `formatRate`, `formatRange`, `formatAsOf`, `formatAsOfDay`) are null-safe (missing or non-finite prints "n/a"), use one locale (en-IN) and India time, and print a figure that rounds to zero without a sign.

## The bundle guard
`npm run size` (called by `scripts/check.sh` after `npm run build`) reads `dist/index.html`, takes the entry script and every `modulepreload` chunk, gzips each (zlib level 6, as `gzip -c`), prints every chunk and the total against 200 KB, and exits 1 above the budget, when a chunk the page names is missing, or when no module script has a `src` (a guard that cannot read the build fails; it never reports a smaller number). `node scripts/size.mjs <dist dir>` measures another build.

**What the limit covers, and what it does not.** The guarded number is entry plus `modulepreload` chunks, as P8-00 measured it. The first paint also always fetches the lazy app shell (`AppShell`), the landing route chunk (`today`) and the stylesheet, and preloads a font. The guard measures these too (chunks found by file-name prefix; a prefix that matches none or two throws) and prints an **"always needed at first paint"** total next to the guarded number, plus the font in raw bytes (woff2 is already compressed, so it is not added). Only the guarded number is held to 200 KB. The five largest other lazy chunks are printed for information.

**Warning band.** When the guarded number is within budget but has under 3 KB left, `size.mjs` prints a `WARNING` line and still exits 0. The rule it enforces by convention: **eager code additions (nav entries, route definitions, tokens, `shared/lib`, `shared/ui` exports) spend the budget; lazy screens do not.** The first slice that meets the warning must first free space (candidates: the preload path, splitting the 128.5 KB entry chunk) before it adds eager code.

**Measured 2026-10-24 on this branch's build (main plus this slice): guarded 195.2 KB gzip (entry 128.5 KB plus ten preload chunks), 4.8 KB to spare; always needed at first paint 205.1 KB gzip (guarded 195.2 + AppShell 8.6 + today 0.7 + CSS 0.5); font 48.3 KB raw.** The kit adds nothing to either (no eager code imports it). Planted check: 10,000 characters of random base64 text appended to the entry chunk of that build raised the first load by 8.4 KB gzip to 203.6 KB; `size.mjs` printed OVER by 3.6 KB and exited 1. Chunks that AppShell or `today` import and that are not preloaded are not followed; the always-needed total is therefore a floor.

## Acceptance criteria
1. Given each of `insufficient`, `unavailable`, `not_evaluable`, `empty`, `loading`, `error` when drawn by `MetricValue`, `MetricCell`, `MetricTile` or `DataTable`, then its word and reason are text, the icon is `aria-hidden`, no digit of a figure appears (only the stated n of an insufficient sample), and no element is in the `good` colour in light or dark mode. Tests: `MetricValue.test.tsx`, `DataTable.test.tsx`.
2. Given a `value` or `provisional` whose number is `NaN` or infinite, or a non-value carrying a stray number, then "Not available" is drawn, never "NaN", "Infinity" or 0. Tests: `MetricValue.test.tsx`, `metric.test.ts`.
3. Given a `provisional` figure then the number, the word "Provisional" and the reason are shown and the colour is not `good`.
4. Given a real zero then it is drawn as a number; given a missing value (`null`, `undefined`) then `metricOf` yields `unavailable` with its reason.
5. Given an `EvidenceBlock` then n, range, basis and (if set) provisional, data used and limits are labelled text in a named section; missing n / range / basis say so; limits are rendered as text, markup is not interpreted; a compact one-line form exists. Test: `EvidenceBlock.test.tsx`.
6. Given a `DataTable` then it is a named table with column and row headers, scrolls in a labelled region reachable by Tab, shows the evidence under it, and shows "No records yet", "Loading" (busy) or "Could not load" instead of an empty body. Test: `DataTable.test.tsx`.
7. Given `VerdictChip` then MET, NOT MET and NOT EVALUABLE are words with an icon, and only MET is in the `good` colour. Test: `VerdictChip.test.tsx`.
8. Given the report formatters then missing input prints "n/a", signs are text, a figure that rounds to zero has no sign, dates are as written and moments are IST. Test: `format.test.ts`.
9. Given a build whose first load is over 200,000 bytes gzip, or whose page names a chunk that is missing, or has no module script with a `src`, then the measuring function reports over budget or throws, and `size.mjs` exits 1; a 10 KB import planted into the entry chunk of a build 4.7 KB under budget turns it over. Tests: `src/test/bundleGuard.test.ts` (fixture build, in memory) and the manual run on a planted copy of `dist` above.
10. The guard also computes and prints the always-needed total (shell, landing route, CSS; font raw), fails if one of those chunks cannot be found exactly once, and prints a WARNING (exit 0) under 3 KB of headroom. Tests: `bundleGuard.test.ts`.
11. `scripts/check.sh` runs the guard after the build; `npm run check` is unchanged. Navigation labels: Learning and Audit log show phase P8.

## Contract
- **API:** none. **Data:** none.
- **UI:** no route. Prototype match: tables and chips follow `docs/prototype` (`.tbl` header caps on the alternate surface, pill chips with a word, centred muted "empty" text) through the existing MUI theme overrides; colours come from palette tokens (`text.secondary`, `warning.main`, `error.main`, `success.main`), no hex in a component.
- **Events/jobs:** none.

## Non-functional
No runtime cost until a screen imports the kit (not in the entry chunk). The guard adds about one second to `check.sh`. No log events.

## Test plan
Vitest and Testing Library, queried by role, label and text; no network, no clock. Every kind of `MetricState` through every component; positive controls for the colour check; the guard on an in-memory fixture (entry, two preload chunks with attributes in either order, a duplicate, a font and a stylesheet that must not count); exact-at-budget and one-byte-over; missing chunk and missing entry. Manual: the guard run on the real build and on a planted copy.

## Open questions
None for this slice. Carried to later slices: the server's metric shape (P8-06), the heatmap chart type and `useUrlState` (P8-02), per-route chunk budgets (set with the first route that needs one).

## Done when
- [x] All acceptance criteria have evidence (tests named above; planted-build run recorded above)
- [x] `scripts/check.sh` green (2026-10-24, first run, no flake); reviewer agent: no blockers/majors (the caller's independent pass, ADR 0004)
- [x] Contract regenerated: not applicable (no API change)
- [x] Design docs/ADR updated (doc 13 §1c, ADR 0098, roadmap row)
