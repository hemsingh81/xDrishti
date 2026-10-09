# Backend rules (.NET 10)

## Architecture
- Layers: `Domain` ← `Application` ← `Infrastructure` ← hosts (`Api`, `Worker`, later `Feed`, `Mcp`, `Cli`).
  `XDrishti.Hosting` holds shared host setup. Rules are enforced by `tests/XDrishti.Architecture.Tests` — never relax them.
- **Domain**: entities/value objects with behaviour, no I/O, no framework packages. Invariants guarded in factory
  methods (`Create`/`Start`); private setters; `sealed` unless designed for inheritance.
- **Application**: one use case = one `IQueryHandler<,>` or `ICommandHandler<>` (internal, sealed), registered
  explicitly in `DependencyInjection.cs`. Ports (interfaces) for every I/O live in `Abstractions/`. Expected failures
  return `Result`/`Error`, never throw. Use `TimeProvider` for time.
- **Infrastructure**: implements ports. One `XDrishtiDbContext`; one schema per module (`Persistence/Schemas.cs`);
  mapping in `Persistence/Configurations/*Configuration.cs`; snake_case naming is automatic.
- **Hosts** are thin: wiring, endpoints, background loops. No business logic in endpoints or hosted services.
- New module = new folder in each layer (+ schema + endpoint module); later its own project when it grows.

## API
- Endpoints live in `Endpoints/*Endpoints.cs` implementing `IEndpointModule`, registered in `ApiSetup.AddApi`.
- Use `TypedResults`, route groups with `.WithTags()`, `.WithName()` (operation id) and `.WithSummary()`.
- Every request/response type is added to `ApiJsonContext` (source-generated JSON). Errors are RFC 7807 problem details.
- Routes: `/api/<module>/<resource>`, plural nouns, kebab-case. After any change run `scripts/gen-api.sh`.

## Data
- Migrations: `dotnet ef migrations add <Name> --project src/XDrishti.Infrastructure --startup-project src/XDrishti.Infrastructure --output-dir Persistence/Migrations`.
  Never edit an applied migration. New schema → call `GrantSchemaUsageToAppRole(schema)` in its migration.
- Reads: `AsNoTracking()`, project to DTOs, paginate. Writes: track only what changes; bulk → Npgsql `COPY`.
- Time series (bars, ticks, valuations) → TimescaleDB hypertables + continuous aggregates (raw SQL in migration).
- Runtime uses role `xd_app` (DML only); migrations run as `xd_owner` via `xd-api migrate`.

## Code
- Warnings are errors; analyzers at `latest-recommended`; style from `.editorconfig`. Do not suppress — fix.
  If a suppression is unavoidable, use `#pragma` at the narrowest scope with a reason.
- Async all the way, `CancellationToken` on every async method; no `async void`; no `.Result`.
- Logging with `[LoggerMessage]` partial methods; structured properties, no string concatenation; never log secrets.
- Options classes with `[Required]`/`[Range]`, bound with `ValidateDataAnnotations().ValidateOnStart()`.
- Primary constructors for DI; `sealed` classes by default; file-scoped namespaces; one public type per file.
- Packages: add versions only in `Directory.Packages.props`; new src project → add a `COPY` line in `Dockerfile`.

## Spikes
- `spikes/` holds throwaway investigations (not shipped, not in images). They follow the same build rules and are tested; useful code
  is promoted into the layers behind existing ports, the rest is deleted when the story is done.

## Commands
- Build: `dotnet build XDrishti.slnx` · Test: `dotnet test --solution XDrishti.slnx` · Format: `dotnet format XDrishti.slnx`
- Run API against the container DB: `scripts/dev-api.sh` (reads the DB password from the Keychain).
