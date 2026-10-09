---
name: new-feature
description: Build one vertical slice end to end (domain → application → infrastructure → API → frontend → tests) from a spec. Use when starting any new capability from a roadmap phase.
---

# New vertical slice

Input: a spec in `docs/specs/` (create it from `docs/specs/TEMPLATE.md` first if none exists, and get the user's OK on
the acceptance criteria before coding). Reference implementation: `GetSystemStatus` + `features/system-status`.

1. **Plan** — list the files per layer and the tests you will write. Keep the slice small enough to finish in one go.
2. **Tests first** where the behaviour is clear: Domain/Application unit tests (`backend/tests/*.Tests/<Module>/`).
3. **Domain** — `backend/src/XDrishti.Domain/<Module>/`: entity/value object with factory methods and invariants; sealed.
4. **Application** — port(s) in `Abstractions/<Module>/`; use case in `<Module>/<UseCase>/<UseCase>.cs` (record +
   `internal sealed` handler returning `Result`); register it in `DependencyInjection.cs`.
5. **Infrastructure** — `Persistence/Configurations/<X>Configuration.cs`, schema constant in `Schemas.cs`, store
   implementing the port; then the `new-migration` skill.
6. **API** — the `add-endpoint` skill (endpoint module, `ApiJsonContext`, `scripts/gen-api.sh`, integration test).
7. **Frontend** — `frontend/src/features/<name>/{api,components,hooks}` + `index.ts`; lazy route in `app/router.tsx`;
   nav entry in `app/navigation.ts`; component test with MSW. Use `shared/ui` pieces before writing new ones.
8. **Verify** — `scripts/check.sh`; then start the stack (`deploy/xd-up.sh`) and look at the screen.
9. **Review** — run the `reviewer` agent on the diff; fix blockers and majors.
10. Update `docs/design` if behaviour differs from it (+ ADR if it is a decision); do not commit unless asked.
