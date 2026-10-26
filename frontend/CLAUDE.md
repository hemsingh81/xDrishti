# Frontend rules (React 19 + TypeScript + Vite + MUI)

## Structure
```
src/
  app/        shell, router, navigation, providers — composition only
  features/   <feature>/{api, components, hooks}/ + index.ts (public API) + *.test.tsx
  shared/     api (typed client + generated schema), ui (reusable components), lib (pure utilities)
  theme/      design tokens + MUI theme (palette from docs/prototype)
  test/       setup, MSW server, fixtures, render helpers
```
- A feature is imported **only** via `@/features/<name>` (its `index.ts`); inside a feature use relative imports.
  `shared/` and `theme/` never import `features/` or `app/`. Enforced by ESLint `no-restricted-imports`.
- New screen = new feature folder + one lazy route in `app/router.tsx` + nav entry in `app/navigation.ts`.

## Code
- TypeScript strict (incl. `noUncheckedIndexedAccess`); no `any`, no non-null `!` outside tests; `import type` for types.
- Function components + hooks only. One component per file, ≤ 250 lines; split when it does two things.
- Server state = TanStack Query (`queryOptions` objects in `features/<x>/api/`, keys `['module', 'resource', …]`).
  No server data in React state or context. Local UI state stays local.
- API calls only through `@/shared/api` (`api.GET(...)` + `unwrap`). Types come from `schema.d.ts` — regenerate with
  `npm run gen:api` (or `scripts/gen-api.sh`), never edit it by hand.
- Styling via the MUI theme and `sx`; colours/radii from `theme/tokens.ts`, never hard-coded hex in components.
- Accessibility: semantic elements, labelled controls, visible focus, status not by colour alone (jsx-a11y enforced).
- Performance: route-level `lazy`; reuse `Intl` formatters (`shared/lib/format.ts`); virtualise long lists; memoise
  only measured hot paths; keep the initial bundle ≤ 200 KB gzip.
  The rule is enforced by `npm run size` (called by `scripts/check.sh` after the build): it holds **entry + modulepreload
  chunks of `dist/index.html`** to 200 KB gzip (1 KB = 1000 bytes). That is not everything the first paint fetches: the lazy
  app shell, the landing route chunk (`today`) and the stylesheet are always fetched too, and the guard prints that larger
  "always needed" total (and the preloaded font in raw bytes) beside the guarded number without limiting it. Eager code
  additions (nav entries, route definitions, tokens, `shared/lib`, `shared/ui` exports) spend the budget; lazy screens do
  not. Below 3 KB of headroom the guard prints a WARNING; the first slice to see it must free space first (the preload path,
  splitting the entry chunk) before adding eager code.
- Errors: every query shows `LoadingState`/`ErrorState`; routes have an `errorElement`.

## Tests
- Vitest + Testing Library + MSW (network-level mocks; unhandled requests fail the test).
- Query by role/label/text as a user would; no implementation details; use `renderWithProviders` / `renderRoutes`.
- Fixtures via builders in `src/test/fixtures.ts`.
- **Tests never depend on the real clock** (ISS-07). A test or file that shows or compares a moment (relative time, a day
  boundary, a date field that refuses the future) calls `pinClock('<ISO instant>')` from `@/test/pinClock` (registers
  `beforeEach`/`afterEach`; fakes only `Date`), or `setNow` in a test that moves between moments, and builds its expected values
  from that instant. `src/test/clockGuard.test.ts` fails a file that asserts "… ago" / "just now" / "in N minutes" /
  "yesterday" without pinning (explicit exceptions listed there); fake timers count as a pin only with a `now:`. The guard
  covers relative-time wording only: a date field, a key expiry or a day boundary is found only by `test:clock`.
- When touching time-dependent code or tests, run `npm run test:clock`: the whole suite at several shifted instants
  (`XD_TEST_NOW=<ISO instant>` fakes `Date` for every test that does not pin it; unset = real clock). `npm run test:clock -- <instant>…`
  runs chosen instants. A failure at some instant means a test reads the real clock: pin it. It is manual (about 15 minutes
  for the default instants) and not part of `npm run check`. Fixtures must not expire: the API key of the account fixtures
  runs until 31 Dec 2099; a test that needs an expiring key builds the date from its pinned instant.

- **Browser runner (manual, P8-16, ADR 0124):** `npm run test:browser` drives the installed Chrome (Playwright, axe) over
  `vite preview` of the production build on 127.0.0.1:4173, answers the API from the MSW handlers (`e2e/api.ts`), runs the
  checklist C0 to C16 per screen, width, scheme and state, and writes `data/p8-evidence/browser.jsonl` plus
  `browser-checklist-summary.md`. `npm run test:browser -- --grep P8-08` runs one screen; `npm run test:browser:perf` the
  throttled timing. It is not part of `scripts/check.sh`; `e2e/` is type-checked and linted by `npm run check`. A new
  screen adds its row to `e2e/matrix.ts` (route and fixtures).

## Commands
`npm run dev` (proxies /api to the stack on :8080) · `npm run check` (typecheck, lint, format, tests) · `npm run build`
