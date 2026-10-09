---
name: new-migration
description: Add an EF Core migration (new table, column, schema, hypertable). Use for every database change.
---

# New migration

1. Change the entity and its `Configuration` (`Infrastructure/Persistence/Configurations/`). New module → add a schema
   constant in `Schemas.cs`.
2. From `backend/`, with the pinned tools (`dotnet tool restore` once):
   ```bash
   dotnet ef migrations add <PascalCaseName> \
     --project src/XDrishti.Infrastructure --startup-project src/XDrishti.Infrastructure \
     --output-dir Persistence/Migrations
   ```
3. Review the generated `Up`/`Down`. Never edit a migration that has been applied or committed — add a new one.
4. New schema → call `migrationBuilder.GrantSchemaUsageToAppRole(Schemas.<X>)` so `xd_app` can use it (DML only).
5. Time-series table (bars, ticks, valuations) → after `CreateTable`, add raw SQL `create_hypertable(...)` and, for
   aggregates, continuous aggregates; choose the chunk interval and compression policy deliberately.
6. Indexes for every query path used by a handler; name keys/indexes explicitly when long.
7. Integration tests apply all migrations on a fresh TimescaleDB — run them (`scripts/check.sh`).
8. Mention the migration in the change summary; the stack applies it through the one-shot `xd-migrate` service on the next
   `deploy/xd-up.sh`.
