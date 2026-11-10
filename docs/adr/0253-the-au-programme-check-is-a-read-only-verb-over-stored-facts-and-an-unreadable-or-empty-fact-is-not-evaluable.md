# 0253 — The AU programme check is a read-only verb over stored facts, and an unreadable or empty fact is NOT EVALUABLE

- **Status:** Accepted (spec AU-19, approved by the owner 2026-11-09).
- **Date:** 2026-11-10.
- **Relation to other ADRs:** Follows the phase-check pattern of ADR 0080, 0097, 0126, 0169. Applies [0240](0240-missed-days-are-recovered-by-one-dependency-ordered-pass-and-each-service-declares-how-it-can-be-recovered.md) and [0244](0244-one-auto-switch-runs-the-chain-and-only-suggests.md).

## Decision
1. `xd auto check` is a verb of the Api host (`scripts/xd.sh auto check`). A handler reads counts and flags through one select-only port; a pure Domain function judges five exit items: a multi-day recovery in the ledger, the removal ADR 0245, at most eight Essential settings, a finished simulate-everything job whose chunks are all training runs cut before the hold-out, and a published night with suggestions while Auto was ON.
2. Each item is MET, NOT MET or NOT EVALUABLE. A fact that cannot be read (missing table, no privilege, ADR folder not visible to the host) and a fact with nothing stored are NOT EVALUABLE, never MET. The programme verdict is MET only when all five are MET, NOT MET when any is, NOT EVALUABLE otherwise.
3. Every read runs in a `READ ONLY` transaction. The verb writes nothing, changes no setting and places no order. Auto ON is read from the insert-only switch log; the automatic decision stays recorded as `auto` there and is never counted as an owner approval.
4. A night counts only when it was published, not made up by a recovery pass, started while the newest switch change before it said ON, and has at least one primary ticket.

## Limits
A multi-day recovery means one succeeded item with its own run covering more than one session; the sessions are not recounted against the calendar. The ADR check looks for a file `0245-*.md` and needs the folder (`Auto:Check:AdrFolder`, set by `scripts/xd.sh`); inside a container it reads NOT EVALUABLE.
