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
- Errors: every query shows `LoadingState`/`ErrorState`; routes have an `errorElement`.

## Tests
- Vitest + Testing Library + MSW (network-level mocks; unhandled requests fail the test).
- Query by role/label/text as a user would; no implementation details; use `renderWithProviders` / `renderRoutes`.
- Fixtures via builders in `src/test/fixtures.ts`.

## Commands
`npm run dev` (proxies /api to the stack on :8080) · `npm run check` (typecheck, lint, format, tests) · `npm run build`
