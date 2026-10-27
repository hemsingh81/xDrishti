# xDrishti — working notes for Claude

Personal, single-owner NSE trading & portfolio system. Design is complete (`docs/design`); implementation follows
the roadmap phases in `docs/design/18-roadmap.md`. Engineering standards: `docs/engineering/README.md`.

## Layout
- `backend/` .NET 10 modular monolith (`src/XDrishti.*`, `tests/`) — rules in `backend/CLAUDE.md`.
- `frontend/` React 19 + TypeScript + Vite + MUI — rules in `frontend/CLAUDE.md`. Match the UX of `docs/prototype/`.
- `deploy/` Podman Compose stack, Caddy, DB init, `xd-up.sh` — rules in `deploy/CLAUDE.md`.
- `eval/` the assistant eval: synthetic fixture, question sets, thresholds (ADR 0143); results go to `data/eval/` (git-ignored).
- `scripts/` developer scripts; `data/` local runtime data (git-ignored).
- `docs/design/*.md` is the source of truth; `docs/site/` is generated — never edit it by hand.
- Trading configuration and strategies are **not files in the repo**: they live in the database, versioned, and are
  managed in the app (ADR 0002).

## Agentic workflow (ADR 0004)
- Work from an **Approved** spec in `docs/specs/` (copy `TEMPLATE.md`); unclear requirement → ask the user.
- Use the subagents in `.claude/agents/` (`backend-dev`, `frontend-dev`, `test-writer`, `reviewer`) and the skills
  `/new-feature`, `/add-endpoint`, `/new-migration`, `/phase-done`.
- Finish every code change with `scripts/check.sh` green and an independent `reviewer` pass. Do not commit or push
  unless the user asks (standing authorisation below).
- Apply the `ponytail` skill (level `full`) on every coding task: smallest complete change, never at the cost of
  tests, validation, security or the spec's acceptance criteria.

## Standing run instructions (owner, 2026-10-22)
- Work in parallel where slices are independent (subagents / worktrees, never two writers on the same files).
- Work on a work branch (e.g. `feature/<phase>-<slice>`), merge back to `main` at every logical point.
- Deploy (`deploy/xd-up.sh`) after every such merge.
- Go as fast as safely possible; keep going in a loop through the roadmap, slice after slice, without waiting
  between slices. Still stop for a genuine owner decision, a red `scripts/check.sh`, or a reviewer blocker.
- **Approvals have an auto option (owner, 2026-10-23).** Anything that needs manual approval must also be able to run
  automatically, controlled by a setting: setting ON = no manual approval needed, setting OFF = manual approval. The
  setting defaults to OFF (manual). Turning it on is an owner act (authenticated, password-protected as designed), the
  auto decision is recorded as auto (never as an owner approval) with the setting snapshot, `unavailable` inputs never
  count as a pass, and no setting ever allows an order to be placed (P7 places none).
- This authorises commit + merge to `main` + deploy for these slices; push to a remote still needs an explicit ask.

## Non-negotiables
- SOLID, modular, reusable, testable, fast — see `docs/engineering/README.md`. Prefer extending over modifying.
- Every change: tests + `scripts/check.sh` green. API change → `scripts/gen-api.sh` in the same change.
- Design decision changed → update `docs/design` and add an ADR in `docs/adr/`; then `scripts/build-docs.sh`.
- No secrets in the repo, images, logs or prompts — Keychain → secret files via `deploy/xd-up.sh`.
- Local only (Podman Desktop); every published port binds to 127.0.0.1 until login exists.
- Short names: `xd` (CLI), `xd-*` (containers), `XDrishti.*` (.NET), `xdrishti` (database, Keychain paths).
- Pin exact versions (NuGet in `Directory.Packages.props`, npm with `save-exact`, image tags in compose/Dockerfiles).
