# scripts

Developer helpers (macOS). They use the .NET SDK in `~/.dotnet` and Podman Desktop.

| Script | What it does |
|--------|--------------|
| `check.sh` | **All quality gates**: backend format/build/tests (incl. integration tests on Podman), frontend typecheck/lint/format/tests/build. Must pass before every commit. |
| `gen-api.sh` | Regenerates the OpenAPI contract and the frontend's typed API client. Run after any API change. |
| `dev-api.sh` | Runs the API on the host with hot reload (port 5080) against the containerised database. |
| `build-docs.sh` | Rebuilds the HTML docs (`docs/site`) from `docs/design/*.md`. |
| `serve.sh` | Serves the clickable prototype (8766) and the docs site (8765). |

The stack itself starts with `deploy/xd-up.sh` and stops with `deploy/xd-down.sh`.

Frontend dev server (hot reload, proxies `/api` to the stack): `cd frontend && npm run dev` → http://localhost:5173.
