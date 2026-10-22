# 0061 — The `xd` CLI is a verb of the Api host behind a thin script

- **Status:** Accepted (spec [P5-07](../specs/P5-07-the-xd-cli.md), approved under the owner's standing run instruction of 2026-10-22; built the same day). The text of this ADR has not been read by the owner.
- **Date:** 2026-10-22.
- **Relation to other ADRs:** [ADR 0041](0041-the-simulate-verb-the-export-library-and-the-pipeline-in-the-host.md) (verbs in the Api host), [ADR 0057](0057-the-lab-api-validate-store-and-read-strategy-versions.md), [ADR 0058](0058-lab-runs-the-quick-test-the-run-identity-and-the-restart-rule.md).

## Context
Doc 11 promises `xd strategy test file.yaml`. Checked: there is no `xd` binary and no `XDrishti.Cli` project; command-line work is done by verbs of the Api host started by `scripts/*.sh`.

## Decision
1. A verb `xd` (first argument alone selects it), parsed by `XdOptions`, run by `XdCommand` in `XDrishti.Api/Cli`, on the host of the other verbs (built, not started, logs to stderr). `scripts/xd.sh strategy|lab ...` starts it.
2. It calls the Lab's use-case handlers: validate, create (store), start, get, list, cancel. It never runs the dispatcher: the running `xd-api` executes the run (ADR 0058); the verb polls. A stack that is down leaves the run queued and the verb says so after 30 s.
3. Exit codes 0, 1, 2, 3, 4 (full test not PASS), 130; located problems on stderr as `file:line:col: [path] severity code message`.
4. No new endpoint, table or secret.

## Consequences
- Needs the stack up for `test`; `validate` and a bad command line need only the host.
- A published `xd` binary (System.CommandLine) stays possible later; the handlers do not change.
- Not exercised against a live database in this slice.
