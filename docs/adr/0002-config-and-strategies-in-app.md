# 0002 — Configuration and strategies are managed in the application

- **Status:** Accepted
- **Date:** 2026-10-08

## Context
Trading configuration (risk, selection, learning, smart rules, schedules…) and strategies (built-in and your own)
change at runtime, are validated, versioned and approved, and are used by background services. Keeping them as
files in the repository (`config/`, `strategies/`) would create a second source of truth next to the database
and mix runtime content with source code.

## Decision
- **Database is the only source of truth.** Trading configuration lives in `config.config_versions`; strategies in
  `lab.strategies` / `lab.strategy_versions` (spec + hash); C# plugins as stored source compiled in `xd-sandbox`.
  Every change is versioned, validated against JSON Schema, diffed, activated and audited.
- **Managed only in the app:** Config screen and Settings (Monaco + schema, diff, activate), Strategy Lab (rules
  editor, form builder, assistant drafts, upload / download of a strategy file), Services, Accounts, Learning.
- **Defaults ship inside the backend** (embedded seed resources in `XDrishti.Infrastructure`) and are written to the
  database on first start. JSON Schemas are generated from the backend's types and served to the frontend.
- **Import / export, not folders:** a strategy or config version can be exported to / imported from a YAML file
  in the app or with the `xd` CLI (`xd strategy test <file>`, `xd config export`). There is no watched strategy folder.
- **Backups** (pg_dump via restic) cover configuration and strategies together with all other data.

## Consequences
- No `config/` or `strategies/` folders in the repository; the CSV `data/inbox/` stays because it is a data drop,
  not configuration.
- Version history, diffs and rollback for config and strategies are features of the app (and of the backups),
  not of git.
- Tests use the embedded defaults and builder helpers instead of files.
