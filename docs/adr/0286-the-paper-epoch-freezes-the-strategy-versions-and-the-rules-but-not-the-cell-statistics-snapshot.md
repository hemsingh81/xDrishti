# 0286 — The paper epoch freezes the strategy versions and the rules but not the cell-statistics snapshot

- **Status:** Accepted (owner, 2026-11-13, roadmap open decision 9, option (a)).
- **Date:** 2026-11-13.
- **Relation to other ADRs:** Amends [0119](0119-a-measurement-epoch-freezes-the-measured-configuration-changes-are-detected-classed-and-labelled-and-nothing-is-applied-or-blocked.md) (the snapshot is no longer frozen) and [0277](0277-the-cell-statistics-refresh-repeats-the-newest-snapshot-to-the-hold-out-cut-fails-closed-and-resumes-only-with-the-owners-password.md) sections 7 and 11 (the paper blocker and the restart of the count). Spec: LC-01 *Decision 9*, P10-05 amendment.

## Context
A 12-week frozen cell snapshot can never pass the nightly selection gate, whose age rule is 5 sessions. With the LC-01 refresh on, every run restarted the paper count; with it off, the nightly plan failed.

## Decision
1. `cells.snapshot` is classified `free`: a new snapshot is a `noted` change and never starts an epoch.
2. A new frozen component `cells.basis` holds the SHA-256 of the options and, per source, the strategy definition hash, universe, start, fills, cost choice and the style and exit labels, without the window end. A refresh only moves ends and keeps the basis; a recompute with other sources or options changes it and starts a new epoch. Its first appearance is `free`, so the deploy does not restart an open epoch. A source run that cannot be read makes the group unreadable (the capture is partial), never a guess.
3. `cells-refresh` leaves `PaperConfigKeys.LearningWriterServices`; `cells`, `retrain` and `weekly` stay.
4. The epoch view (`cellsRefreshed`) and the P10-06 phase check (`cells_refreshed: N` caveat) count the noted snapshot changes of the epoch. The P10-04 reference is unchanged.
5. The age rule stays 5 sessions. No order is placed; nothing here loosens a gate.

## Consequences
- The paper count measures the frozen versions and rules with statistics refreshed from research runs, not one fixed snapshot (P10-05 question 2 had rejected that; the owner chose it).
- The Auto evidence block says each refresh is noted in the paper epoch.
- Each source's identity in the basis is read from the stored record (the replay baseline): definition hash, parameters, timeframe, run data options, csv flag, fills, cost choice with capital and profile, exit and time stop, universe and start. A record the reader cannot rebuild (for example fills with a delay) makes the cells group unreadable (fail closed).
- **One-time deploy window:** the first appearance of `cells.basis` is `free`, so the owner should not run `xd learn cells` with other sources or options across the deploy; that change would be absorbed instead of starting an epoch.
- **Blind captures:** a snapshot whose source run is not a research run (before AU-11) or cannot be rebuilt makes the cells group unreadable, and the captures are partial. After the deploy check that `GET /api/paper/config` shows no unreadable groups.
- Not in the basis yet: the regime and universe splits and the Confidence/Shrinkage options. Add them once a gate or the selection reads them.
- The epoch view counts only refreshes whose capture holds no frozen change.
- **Record-schema bump:** a record whose schema is not the current one is refused by the replay reader, so a schema bump blinds the paper capture (cells group unreadable) until the sources are simulated again. Upgrade path: read the identity fields schema-tolerantly.
- **Instrument list and basket identity:** a list is hashed by its member ids (the request itself, stable across refreshes); a basket by its id and the universe label. The profile's stored risk per trade and capital cap are in the cost identity.
- **Follow-up (dated sources):** the refresh re-simulates a dated (members-as-of) source as a "members now" run, because the replay baseline drops `MembersBasis`. With the label in the basis this shows honestly as a frozen change. Follow-up: the refresh refuses dated sources (`not_rebuildable`) or carries `MembersBasis`.
