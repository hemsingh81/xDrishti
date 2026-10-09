# Engineering standards

These rules apply to all code in this repository. The per-folder `CLAUDE.md` files (root, `backend/`,
`backend/tests/`, `frontend/`, `deploy/`) hold the detailed, layer-specific rules; this page is the overview.
Most rules are **enforced by tooling** — a rule that can be automated should be.

## 1. Principles

| Principle | What it means here |
|-----------|--------------------|
| **SOLID** | *S* — one reason to change per class/component/hook (one use case = one handler). *O* — extend by adding a module, handler, endpoint module or feature folder, not by editing a switch. *L* — implementations honour their interface contract (tests run against the interface). *I* — small, focused interfaces (ports) owned by the layer that uses them. *D* — depend on abstractions; composition happens only in hosts (`Program.cs`, `App.tsx`). |
| **Modular** | Backend: one PostgreSQL schema and one folder per module; `Domain ← Application ← Infrastructure ← hosts`. Frontend: `features/<name>` with a public `index.ts`; `shared/` never imports features. |
| **Reuse** | Shared building blocks live in one place (`Domain/Common`, `XDrishti.Hosting`, `shared/ui`, `shared/lib`, `shared/api`). Before writing a helper, search for an existing one; a second copy must be extracted. |
| **Testable** | No static state or hidden I/O; time via `TimeProvider`, I/O behind ports; pure engine code. Every change ships with tests (see §3). |
| **Performance** | Measured, not guessed — but the defaults below are mandatory. |
| **Secure by default** | Secrets only from Keychain → secret files; least-privilege DB role; containers non-root, read-only; all ports on 127.0.0.1. |
| **Simple** | No speculative abstractions, no frameworks "for later". Delete dead code. |

## 2. Performance defaults

- **Backend:** async all the way (no `.Result`/`.Wait()`), `CancellationToken` everywhere; `AsNoTracking()` for reads;
  project to DTOs in queries (no loading whole graphs); one pooled `NpgsqlDataSource`, pooled `DbContext`; bulk
  writes with Npgsql `COPY`; source-generated JSON and `[LoggerMessage]` logging; no exceptions for expected
  failures (use `Result`); Server GC; TimescaleDB hypertables/continuous aggregates for time series.
- **Frontend:** route-level code splitting (`lazy`), TanStack Query caching with explicit `staleTime`; virtualised
  lists for large tables; memoise only measured hot paths; reuse `Intl` formatters; no layout thrash in charts;
  bundle budget: initial JS ≤ 200 KB gzip.
- **Infra:** multi-stage images, cached restores, chiseled/alpine runtime images, compression and immutable
  caching for hashed assets at the proxy.

## 3. Testing

| Level | Backend | Frontend |
|-------|---------|----------|
| Unit | Domain & Application (xUnit v3, Shouldly, NSubstitute, `FakeTimeProvider`) | hooks, utils, components (Vitest, Testing Library) |
| Contract / integration | API + real TimescaleDB via Testcontainers (Podman) | API mocked at network level with MSW |
| Architecture | NetArchTest layer rules | ESLint import boundaries |

Rules: test behaviour, not implementation; one reason to fail per test; no sleeps; tests run in parallel.

## 4. Definition of done

1. `scripts/check.sh` passes (build with warnings as errors, all tests, lint, format, type check).
2. New behaviour has tests; bugs get a regression test first.
3. API changes: contract regenerated (`scripts/gen-api.sh`) and frontend types updated in the same change.
4. Database changes: an EF Core migration (never edit an applied one); time-series tables reviewed for hypertables.
5. Design changes: `docs/design` updated; significant decisions recorded in `docs/adr`.
6. The stack still starts with `deploy/xd-up.sh` and System status is green.
