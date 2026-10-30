# 0168 - the execution screens' read model is GET only, derived from insert-only rows, and says unavailable rather than zero

- **Status:** Accepted (decided in P12-10a under the owner's standing run instruction; spec [P12-10](../specs/P12-10-execution-screens-and-notifications.md), criteria 1 to 5, 10, 11, 13, 17, 18).
- **Date:** 2026-10-31.
- **Relation to other ADRs:** builds on ADR 0156 (`xd_app` only reads schema `execution`), 0166 (tickets), 0167 (limits sheet, ledger, action log) and 0027 (bell items).

## Decision
1. Five GET routes (`overview`, `blotter`, `positions`, `activity`, `notices`) and four fields on `status` (`anyAutomated`, `killSwitchActive`, `pollSeconds`, `marketOpen`). No write, no table, no grant, no migration.
2. Every figure is `{state, value?, cap?, reason?, n?}`. An input that cannot be read is `unavailable` with a reason, never 0. Caps are the limits sheet in force; the pending raise sits beside it. An unset cap is `value` with `cap` null and the reason `cap_unset` or `no_limits_sheet`.
3. Worst-case loss = max(0, minus the day mark's realised) + the open risk to the stops from our entry fills (stops assumed to fill at their price); no day mark with filled entries, or exits above entries, is `unavailable`.
4. Protection is `confirmed` only when the intent is protected or managing, no protection alarm is open, and the newest readable reconciliation run of the account is at most 120 seconds old with no break on the symbol; `missing` only on an open `protection_breach` alarm; otherwise `unknown`. Foreign positions are never confirmed.
5. Notices are derived on every read (nothing stored); ids include the cause; a resolved item stays 24 hours. `exec_auto_action` and `exec_day_summary` are born resolved. `exec_attended_pause` has no source (the owner-presence source is closed) and is not emitted.
6. `anyAutomated` = execution enabled and an account configured `semi_auto` or `auto_live` (none can be today); `pollSeconds` is 5 while that holds and the session is open, else 60.
7. Slippage is per fill against the ticket price, positive when worse, in ticks (instrument master tick; `unavailable` without one) and percent.
8. Log ids 6730 to 6735 (counts and codes only).

## Known limits
Price freshness uses the minute bars (a bar inside 5 s plus one minute; `exec.feed_max_age_s` is not read). Accounts listed are those the mode ladder lists. The canary dates (C1, C2) have no source yet. No index was added: day-sized tables.
