# frontend — React 19 + TypeScript + Vite + MUI

Rules: [`CLAUDE.md`](CLAUDE.md) · UX reference: [`docs/prototype`](../docs/prototype/index.html).

| Task | Command |
|------|---------|
| Install | `npm ci` |
| Dev server (hot reload; `/api` proxied to the stack on :8080) | `npm run dev` → http://localhost:5173 |
| All checks (types, lint, format, tests) | `npm run check` |
| Production build | `npm run build` |
| Regenerate API types | `npm run gen:api` (after `../scripts/gen-api.sh` regenerated the contract) |

Structure: `src/app` (shell, router, navigation) · `src/features/<name>` (feature + public `index.ts`) ·
`src/shared/{api,ui,lib}` · `src/theme` · `src/test`. In production the build is served by `xd-proxy` (Caddy).

Authentication (`src/features/auth`): every route except `/login` sits behind `RequireAuth`; the session cookie
(`xd.sid`, HttpOnly) is set by the API, the CSRF token lives only in the TanStack Query cache (never in web storage)
and is attached by the shared client to unsafe requests. In `npm run dev` the Vite proxy forwards cookies unchanged,
so sign in through http://localhost:5173/login against the stack on :8080 (the static `/prototype/` and `/docs/` are
served by the proxy only on :8080).
