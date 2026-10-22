# P5-07 — The `xd` CLI

- **Status:** Approved (owner's standing run instruction of 2026-10-22; every open question as recommended). Built 2026-10-22 ([ADR 0061](../adr/0061-the-xd-cli-is-a-verb-of-the-api-host-behind-a-thin-script.md)).
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice 7 of [P5-00](P5-00-strategy-lab-plan.md).
- **Design refs:** docs/design/11-strategy-lab.md §1 (Upload / CLI), 03-target-architecture.md (CLI row) · ADR 0041 (the `simulate` verb), ADR 0057, ADR 0058.

## Assumptions (recommended answers taken)
1. **Packaging, verified 2026-10-22: there is no `xd` binary.** No `XDrishti.Cli` project exists (doc 03 lists one as a plan); the only command-line surface is the verbs of the Api host (`simulate`, `cost-sheet`, `verify-pack`, `migrate`, `owner-reset`) started by thin `scripts/*.sh`. The smallest packaging consistent with that is a new verb `xd` in the same host plus `scripts/xd.sh`. A separate tool project (System.CommandLine, a published binary) is deferred.
2. **In-process or API?** Neither a `LabRunner` in the CLI process nor HTTP (no session cookie exists for a script). The verb calls the Lab's own use-case handlers (the ones the endpoints call) to store the version and queue the run; the running `xd-api` carries the run out (ADR 0058) and the verb polls the run. Running the dispatcher in the CLI would claim other people's queued runs and mark runs `interrupted` under host name `xd-api`.
3. Universe by `--symbols` or `--basket-id` (a GUID); a basket by name is not added.
4. Exit codes: 0 done (full test: PASS), 1 refused, 2 usage, 3 failure or timeout, 4 full test finished MARGINAL or FAIL, 130 cancelled.

## Goal
`xd strategy validate FILE`, `xd strategy test FILE ...` and `xd lab runs` from the terminal, with located problems on stderr and a script-usable exit code.

## Scope
**In:** the verb and its parser; `scripts/xd.sh`; tests; ADR 0061; docs. **Out:** a published binary, a basket by name, `--no-wait`, upload of results, promotion, any new endpoint or migration.

## Acceptance criteria
1. Only the first argument `xd` selects the verb (dispatch test).
2. `validate` prints `file:line:col: [path] severity code message` for each problem on stderr and exits 1; a good file prints id, version, hash and exits 0; a missing or oversized file (64 KB) is refused before any call.
3. `test` stores the version (idempotent by hash), queues the run, waits, prints stage changes on stderr and the run, labels, counts, simulation run keys and (full) the verdict with each gate's reason on stdout.
4. Exit by outcome: PASS or quick succeeded 0; verdict MARGINAL/FAIL 4; run failed with located problems 1; interrupted, other failure or timeout 3; refused post 1.
5. Ctrl-C while waiting cancels the run and exits 130.
6. A wrong command line prints the errors and the usage and exits 2.
7. No secret is printed or logged: the only secret is the database password from the Keychain, read by the script into the environment.

## Contract
No API, data or UI change. Verb `xd` of `XDrishti.Api`; `scripts/xd.sh`.

## Non-functional
Poll every 2 s; default timeout 60 minutes (`--timeout-minutes`, at most 1440); a timeout leaves the run going and says how to read it.

## Test plan
Unit-style tests in `Api.IntegrationTests/Cli` with fake handlers (no database): dispatch, parser table, each exit code, Ctrl-C. Not covered: a real run through a live `xd-api` and database.

## Open questions
None for the owner.

## Done when
- [x] Tests green; `scripts/check.sh` green
- [x] ADR 0061, doc 11, roadmap row, scripts README, `scripts/build-docs.sh`
