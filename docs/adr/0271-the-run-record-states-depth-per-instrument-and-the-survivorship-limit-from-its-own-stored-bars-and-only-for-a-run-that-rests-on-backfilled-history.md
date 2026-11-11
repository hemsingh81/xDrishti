# 0271 — The run record states depth per instrument and the survivorship limit from its own stored bars, and only for a run that rests on backfilled history

- **Status:** Proposed (spec [CF-04](../specs/CF-04-history-backfill-to-five-years-daily-bhavcopy-and-corporate-actions.md) Part C, approved 2026-11-11). Becomes Accepted when the owner has read it.
- **Date:** 2026-11-11.
- **Relation to other ADRs:** follows [ADR 0266](0266-dated-members-are-an-opt-in-basis-of-a-basket-run-a-tier-observation-is-proposed-as-a-scope-hypothesis-and-never-applied-and-the-universe-lens-tags-trades-at-read-time.md) (a label says "point in time" only for dated members) and [ADR 0263](0263-relative-strength-features-are-stored-rows-of-the-previous-closed-session-with-a-closed-parameter-set.md) (omit when empty, so keys and goldens do not move).

## Decision
1. **Depth is derived, not stored.** Every instrument record already holds `first_bar` and `last_bar` (the historyFrom of AC 12) and the run key already holds the bars hash. `HistoryDepth` (Domain, pure) takes years = (min(last bar, run end) - first bar) / 365.25 over the instruments that ran, so nothing after the run's last date counts (no look-ahead) and no field, key line or schema version changes.
2. **Buckets:** under 2, 2 to under 3, 3 to under 5, 5 or more years.
3. **Lines only when they say something new.** `RunLabels` adds a `depth:` line (earliest first bar, the bucket counts) and a `survivorship:` line only when at least one instrument that ran is 3 years deep or more (the stored history before the backfill is about 2.1 years). Every run made on the old history has the same labels, record and export as before; no stored text is rewritten.
4. **Survivorship wording is honest.** Single snapshot or no basis: "members as of today; instruments delisted in the window are absent (Dhan serves active instruments only; ISS-22)". Dated members: the members are per day but instruments Dhan no longer serves have no 1-minute bars. "Point in time" is never printed by this line; only `UniverseBasisRecord` may print it, and only for dated members.

## Consequences
The per-instrument depth in the History card and `GET /api/data/history` is Part B's. Changing the 3-year gate is a one-constant change with its tests.
