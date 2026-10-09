# xDrishti — working notes for Claude

Personal, single-owner NSE trading & portfolio system. Design is complete (`docs/design`); implementation follows
the roadmap phases in `docs/design/18-roadmap.md`. Engineering standards: `docs/engineering/README.md`.

## Layout
- `backend/` .NET 10 modular monolith (`src/XDrishti.*`, `tests/`) — rules in `backend/CLAUDE.md`.
- `frontend/` React 19 + TypeScript + Vite + MUI — rules in `frontend/CLAUDE.md`. Match the UX of `docs/prototype/`.
- `deploy/` Podman Compose stack, Caddy, DB init, `xd-up.sh` — rules in `deploy/CLAUDE.md`.
- `scripts/` developer scripts; `data/` local runtime data (git-ignored).
- `docs/design/*.md` is the source of truth; `docs/site/` is generated — never edit it by hand.
- Trading configuration and strategies are **not files in the repo**: they live in the database, versioned, and are
  managed in the app (ADR 0002).

## Non-negotiables
- SOLID, modular, reusable, testable, fast — see `docs/engineering/README.md`. Prefer extending over modifying.
- Every change: tests + `scripts/check.sh` green. API change → `scripts/gen-api.sh` in the same change.
- Design decision changed → update `docs/design` and add an ADR in `docs/adr/`; then `scripts/build-docs.sh`.
- No secrets in the repo, images, logs or prompts — Keychain → secret files via `deploy/xd-up.sh`.
- Local only (Podman Desktop); every published port binds to 127.0.0.1 until login exists.
- Short names: `xd` (CLI), `xd-*` (containers), `XDrishti.*` (.NET), `xdrishti` (database, Keychain paths).
- Pin exact versions (NuGet in `Directory.Packages.props`, npm with `save-exact`, image tags in compose/Dockerfiles).
