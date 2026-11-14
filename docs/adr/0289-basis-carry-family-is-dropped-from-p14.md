# 0289 — The basis/carry family is dropped from P14

- **Status:** Accepted (owner, 2026-11-13, in chat: "drop and remove").
- **Date:** 2026-11-13.
- **Relation to other ADRs:** Amends [0202](0202-the-p14-phase-check-reads-stored-verdicts-for-the-current-versions-only-closes-a-family-by-a-net-drop-or-a-hold-out-confirmed-keep-counts-every-expression-dropped-as-met-and-finds-a-non-cash-expression-in-use-by-a-select-of-the-stored-cell-keys.md) (the family list).

## Context
P14-14 (futures end-of-day loader and carry model) and P14-15 (basis evaluation) were never built. No futures price history exists in the repo, and the only free source (NSE F&O bhavcopy) would need an allow-list change after the owner reads NSE's terms. The P14 plan (D12) itself expected a DROP verdict. The phase check listed `basis:carry-1` as a family that always read `none stored`, so the P14 exit could not be decided without `--cut basis:carry-1`.

## Decision
1. The basis/carry family is dropped. P14-14 and P14-15 are marked Dropped; nothing of them was built, so no code, table or stored row goes with them.
2. `OptionsPhaseCheck.Families` has six families (`expr:option_buy`, `expr:debit_spread`, `expr:credit_spread`, `expr:iron_condor`, `ext:iv-1`, `pairs:intraday-1`). The P14 exit is decided by those six without a `--cut`. `--cut` stays for them; `basis:carry-1` is now refused as an unknown family.
3. Unchanged: `PAIRS`, the option families, and the `cells.basis` paper fingerprint (an unrelated "basis").

## Consequences
- No migration, no API change, no frontend change, no new setting. Revisit only with a new spec if a futures source is ever approved.
