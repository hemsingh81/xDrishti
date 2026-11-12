# 0278 — The forward-evidence switches are reachable: a shadow form, nine compose variables all off, and a read-only Auto block that resumes nothing

- **Status:** Accepted (coordinator's recommendation under the owner's standing instruction of 2026-11-11; spec OPS-01 as recommended).
- **Date:** 2026-11-12.
- **Relation to other ADRs:** Uses [0211](0211-the-shadow-record-writes-every-nightly-candidate-before-the-open-forward-only-and-insert-only-chained-by-digest-as-a-count-separate-from-the-p10-paper-count-and-the-switch-is-the-owners-and-off-by-default.md)/0212 (the shadow switch route), [0252](0252-the-auto-switch-resumes-five-chain-services-follows-the-stored-switch-for-the-builder-and-restores-only-what-it-resumed.md) and its LC-01 amendment [0277](0277-the-cell-statistics-refresh-repeats-the-newest-snapshot-to-the-hold-out-cut-fails-closed-and-resumes-only-with-the-owners-password.md) (the Auto chain), 0195/0270/0273 (history switches).

## Decision
1. Auto does **not** resume the evidence services (shadow-write, shadow-grade, paper-capture, paper-review, cells-refresh): one switch must not start the irreversible forward count. The Auto screen shows their state read only and links to where the owner acts.
2. The shadow switch gets the P8-19 owner-act form on Paper → Shadow (reason, fresh password, gap acknowledgement after a 409). Same route, no new endpoint.
3. Nine compose variables on `xd-api` make the fetch and admission switches reachable; all default `false` or empty; a variable is configuration, not an owner act. No UI control turns a fetch or admission switch on.

## Consequences
A new default of `true` for any of these needs an ADR. `EvidenceComposeRulesTests` holds the defaults.
