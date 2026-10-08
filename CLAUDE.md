# xDrishti — working notes for Claude

Personal, single-owner NSE trading & portfolio system. Design is complete; implementation follows the
roadmap phases in `docs/design/18-roadmap.md`.

## Layout
- `backend/` .NET 10 modular monolith (`src/XDrishti.*`, `tests/`). Engine modules (Indicators, Strategies,
  Simulation, Learning, Planning) stay pure — no I/O.
- `frontend/` React 19 + TS + Vite. Match the UX of `docs/prototype/` (palette, navigation, components).
- `deploy/`, `scripts/`; `data/` is local runtime data and git-ignored.
- Trading configuration and strategies are **not files in the repo**: they live in the database, versioned, and are
  managed in the app (Config screen, Strategy Lab). Built-in defaults and JSON schemas ship inside the backend
  (see ADR 0002).
- `docs/design/*.md` is the source of truth; `docs/site/` is generated — never edit it by hand.

## Rules
- When implementation changes a design decision: update the design doc and add an ADR in `docs/adr/`.
- After editing `docs/design/*.md`, run `scripts/build-docs.sh`.
- Short names: `xd` (CLI), `xd-*` (containers), `XDrishti.*` (.NET), `xdrishti` (database, Keychain paths).
- No secrets in the repo — broker keys come from the macOS Keychain via Podman secrets.
- Everything runs locally (Podman Desktop); no cloud services.
