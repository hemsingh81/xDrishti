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
