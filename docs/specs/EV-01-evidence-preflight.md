# EV-01 — `xd evidence preflight`: the first-evidence prerequisites in one read

- **Status:** Approved (coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended). Not built.
- **Phase / roadmap row:** EV — evidence, row EV-01 (new; GAP-06). The runbook half of GAP-06 is written: docs/engineering/first-evidence-runbook.md.
- **Size:** about 1 agent-day (estimate) · **Needs:** nothing new; reads stores and settings that exist
- **Safety:** not a SAFETY slice. Read only: it stores, switches, moves and orders nothing.
- **Design refs:** docs/engineering/first-evidence-runbook.md; ADR 0127, 0207, 0211, 0252, 0270, 0272

## Goal
Before and during the runbook, the owner runs one verb and sees which prerequisite of first evidence is met, not met or unknown, with the fix for each.

## Verified facts (main at 30777841)
- Verbs dispatch in backend/src/XDrishti.Api/Cli/XdCommand.cs (e.g. `auto check` :202, `charge-sheet` :192, `data backfill|probe-seam|history` :282); `evidence` is not taken.
- **Deployed version:** the image tag is `XD_VERSION` default `0.1.0` (deploy/compose.yaml:99,103), so the tag says nothing. `GET /api/system/status` returns `Application.Version`, `StartedAt` and `Database.LatestMigration`, `PendingMigrations` (backend/src/XDrishti.Application/Platform/SystemStatus/GetSystemStatus.cs:17,19). No commit is stamped into the image (no `rev-parse` in deploy/xd-up.sh).
- Charge sheet: `xd charge-sheet show` (ChargeSheetCommand.cs:37). Context: `scripts/context.sh coverage` (scripts/context.sh:4). Hold-out: `GET /api/lab/holdout` (LabConfidenceEndpoints.cs:33), fixed at the first Lab post (ADR 0207 §3). Champion: `learn designate --role champion` (LearnDesignateOptions.cs:49). Backfill: `xd data history` (DataBackfillCommand.cs:21). Shadow state: `GET /api/paper/shadow/summary` (PaperShadowEndpoints.cs:19). Auto: `GET /api/auto` (AutoEndpoints.cs:27).
- Cell snapshots are written only by `xd learn cells` (no scheduled service writes them; `LearnCellsCommand` is the only host caller found).
- Replay memory: `Replay:MaxWindowMb` default 300 (SettingsRegistry.cs:97; SystemReplayOptions.cs:41), checked at start (StartReplayHandler.cs:55); xd-api `mem_limit: 1g` (compose.yaml:194); ADR 0272 says the .NET heap in 1 GiB is about 768 MiB and the window must fit inside it (docs/adr/0272-…md:49). A replay is also refused while the backfill is incomplete (same line).

## Decisions (each as recommended)
1. **One verb, host side,** `xd evidence preflight [--json]`, calling the existing query handlers in one scope (like `auto check`), not a new API route. *Recommended*: the owner's runbook is a terminal flow; a screen can read the same use case later.
2. **Three states only:** `MET`, `NOT MET`, `UNAVAILABLE`. A read that throws or a missing store is `UNAVAILABLE`, never `MET`. Exit 0 whatever the states (it is a readout, not a gate).
3. **Deployed version = migrations, not the tag:** `MET` when `PendingMigrations = 0` and `LatestMigration` equals the newest migration compiled into the verb; else `NOT MET` with both names. Stamping a commit is out of scope.
4. **Cell snapshot age** is reported in sessions from the trading calendar; `NOT MET` above 5 sessions. The 5 is a runbook guideline (until LC-01), not a selection rule: selection only requires stats dated before the decision (EligibilityFilters.cs:69-78).
5. **Memory headroom:** the verb runs on the host in the repository, so it reads `xd-api`'s `mem_limit` from `deploy/compose.yaml` (unparseable or missing: `UNAVAILABLE`) and `Replay:MaxWindowMb` from the same configuration `xd-api` binds. `MET` when `MaxWindowMb ≤ 0.75 × mem_limit − 256 MiB` (1 GiB gives 512 MiB, so the default 300 is MET); the line prints both numbers and the largest instrument count by the ADR 0272 formula (53 at 300 MiB).

## Scope
**In:** the verb, one application query `GetEvidencePreflightQuery` composing existing reads, text and JSON output, tests.
**Out:** any write or switch; a UI; the runbook text (already written).

## Acceptance criteria
1. The verb prints exactly these 12 lines, each `MET | NOT MET | UNAVAILABLE` with a reason and a fix: deployed version; charge sheet confirmed at current rates; context probe (index, VIX, regime first readable date); hold-out fixed and its dates; newest cell snapshot age (sessions); champion designated; shadow switch; paper-capture service; Auto switch and chain complete; backfill status (instruments verified / pending / failed); replay memory headroom; NSE history and admit-verified switches.
2. Every read that throws gives `UNAVAILABLE` with the exception type only (no message text, no secret); a test forces each read to throw.
3. Nothing is written: a test counts rows of every touched schema before and after.
4. `--json` prints the same items as an array of `{item, state, reason, fix}`; text and JSON agree (one source).
5. Hold-out not fixed prints `NOT MET` with "fixed at the first Lab post (ADR 0207)"; fixed prints the block start and end.
6. A charge sheet confirmed at another rates hash prints `NOT MET` (stale confirmation).
7. Runs in under 5 s on the owner's database (p95, stated, measured once).

## Contract
- **CLI:** `scripts/xd.sh evidence preflight [--json]`; exit 0, 2 wrong command line, 3 failure, 130 cancelled.
- No API, data, UI or job change.

## Test plan
Unit: each item's state rules (thresholds, UNAVAILABLE on throw). Integration over a seeded DB: all NOT MET on an empty database; all MET on a seeded one; nothing written.

## Risks
The compose file can differ from what is running (edited but not redeployed); the line says "from deploy/compose.yaml". The host verb's configuration can differ from `xd-api`'s for the `Data:History:*` switches (environment overrides); the line says which configuration it read. The 0.75 heap share and 256 MiB margin are assumptions from ADR 0272, not measured.

## Done when
- [ ] ACs 1–7 have evidence; `scripts/check.sh` green; reviewer: no blockers
- [ ] The runbook's step 0 names the verb as built
