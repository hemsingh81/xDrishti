# P7-11 — Pre-open check, arming, plan tickets join the live active set (alerts only; no order)

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22 and the owner's binding decisions of 2026-10-23; every question taken as the recommended answer). Built 2026-10-24.
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, slice P7-11 of [P7-00](P7-00-selection-accounts-and-nightly-plan.md), 3.5 d
- **Design refs:** docs/design/04-accounts-access-and-operations.md §4.1, §4.2 · 10-trade-selection.md §7 · 15 · 19-risks-and-compliance.md §4, §5 · ADR 0083 (plan record: Approved to Armed, Invalidated, Expired), 0086 (services), 0091 (autonomy setting), 0095 (review cutoff), 0024 (active set), ADR 0096

## Assumptions (taken as recommended; the owner's decisions are binding)
1. **No order, ever.** Arming is a state in our own database and a place in the feed's read-only set. The pinned wording (a test pins it): "Armed: watched with alerts. No order is placed and none exists. A stop is only an alert." It is the reason text of every arming row. The no-order tripwire is untouched and green. Nothing here uses a broker write endpoint or a credential.
2. **Paper accounts only** (allocations are paper-only by the database; unchanged).
3. **This slice approves nothing.** It only moves tickets that are already `Approved` (by the owner, or by the optional P7-12 rule when the owner switched it ON); a ticket in any other state is not looked at. It does not read the autonomy settings.
4. **No promotion from the reserve, anywhere (Q5).** There is no code path: the handler never reads the reserve as a source, and an architecture test fails if the pre-open code mentions promotion or the reserve or depends on the autonomy settings. `autonomy.promote_from_reserve` stays a stored setting that nothing reads.
5. **Arming goes through the existing table.** `Approved to Armed` is a service move (`system:preopen`) by `MoveTicketHandler` (`TicketLifecycle` already allows only the services to make it), then the plan `Approved to Armed` (the database needs an armed ticket). The database already refuses any other pair, a missing predecessor or a wrong chain: **no migration is needed.**
6. **The pre-open data are the ones that exist.** The last (pre-open indicative) price and previous close come from the existing read-only market quote (`IQuoteSource`, the primary Data account, the endpoint the holdings poll uses); the average true range (14 sessions) is from the stored day bars before today. **No new allow-list entry, endpoint or credential.** Not verified on a real pre-open morning: whether the quote's last price at 09:10 is the indicative open or the previous close (then the gap is 0 and every ticket passes; see honest limits).
7. **The gap rule.** Invalidate when `|last - previous close| > gap_invalidate_atr x ATR` (default 0.5, doc 16; a constant, no settings home yet). Either direction. "Gap beyond its trigger" is not evaluated: the plan stores the trigger as text only (no numeric level), so it cannot be checked. The F&O ban, surveillance and price band have no data source (P15): they are **not re-checked here** and say so in the run summary; the approval already carried them as `unavailable`.
8. **A missing input never arms.** No quote, no previous close, too few sessions for the range, a failed price read, a symbol the master list cannot map: all invalidate with a stable code (`unavailable_quote`, `unavailable_previous_close`, `unavailable_atr`).
9. **Cap.** At most 50 tickets are armed per session (`PreOpenRules.MaxArmedTickets`); the lowest-ranked excess are invalidated with `armed_cap_exceeded` and a reason naming the cap. The feed has its own cap (`Live:MaxInstruments`): armed tickets rank **after** alerts, indices and holdings, so when that cap cuts, an armed ticket is the one left out (the existing cut warning) and no existing member is displaced.
10. **The owner is told through the suggestions inbox** (insert-only row `ticket_invalidated_preopen`, subject `item-<id>`, one per ticket). No notification port exists; the failure notice of the service is the existing one.
11. **When it runs.** Service `preopen`, daily 09:10 on trading days, starts **paused**, no catch-up; the handler refuses at or after 09:15 India time (`plans.preopen_late`) and changes nothing. It re-reads the plan state each time and touches only Approved tickets, so a second run changes nothing. A plan whose tickets are all invalidated is moved to Expired (not left Approved).
12. **Expiry.** Service `plan-expiry`, daily 15:40 on trading days, paused: tickets still Armed and Approved-never-armed move to Expired as `system:expiry` and the plan to Expired, only at or after 15:30 on the plan's day (or for any earlier day still open: catch-up). Triggered tickets are left alone. A NOTIFY `active_set_changed` follows arming and expiry (best effort; the feed's one-minute recompute is the net).
13. **The feed provider.** `ArmedTicketsProvider` (scoped `IActiveSetProvider`, reason `ArmedTicket`, quote mode) lists the Armed tickets of the plan whose date is today in India, maps them by canonical symbol to the Dhan NSE equity id, and leaves the set when the ticket stops being Armed. If it cannot read the plan record it proposes nothing and logs; it never fails the set. With no armed ticket it adds nothing: the set is what it was (a test compares).
14. **Alerts on the trigger/stop level are a follow-up.** The plan stores the trigger and stop as text rules, not numbers, so a price crossing cannot be evaluated; no notification port exists either. The armed instrument is in the feed with a live price; the nothing-is-ordered wording is in every arming row.
15. **Names.** ADR 0096. Log ids 4780 to 4787. Services `preopen` and `plan-expiry`. No API route, no migration.

## Goal
At 09:10 every ticket the owner approved is checked against the pre-open price; those that pass are armed (watched with alerts, in the feed with a live price) and the others are dropped with their numbers and a note in the owner's inbox; at the end of the session armed tickets expire and leave the feed. Nothing is ordered, approved or taken from the reserve.

## Scope
**In:** Domain (`PreOpenRules`, `PreOpenReading`, `PreOpenDecision`, `PreOpenCodes`, `AverageTrueRange`, `DailyRange`, `PlanWording.Armed`); Application (`RunPreOpenCheckHandler`, `ExpireTicketsHandler`, `PreOpenService`, `PlanExpiryService`, `ArmedTicketsProvider`, ports `IPreOpenSource`, `IArmedTicketReader`, `IInstrumentSecurities`, log ids, `ActiveSetReason.ArmedTicket`, builder rank); Infrastructure (`PreOpenSource`, `InstrumentSecurities`, `PlanStore` armed read); ADR, docs, roadmap.
**Out:** promotion from the reserve (never), approving anything, trigger/stop/target alerts and notifications (follow-up, see 14), numeric trigger gap, ban/surveillance/price band (P15), any order, depth recording (P7-16).

## Acceptance criteria
1. A gap beyond the limit invalidates, with the numbers in the state row and an inbox row; a gap within it arms. *`PreOpenRulesTests`, `PreOpenCheckTests`, `PreOpenArmingDatabaseTests`.*
2. Any missing input (quote, previous close, range, a failed read, an unmapped symbol) invalidates and never arms. *`PreOpenRulesTests`, `PreOpenCheckTests`, `PreOpenSourceTests`.*
3. No promotion path: the pre-open code has no dependency on autonomy settings, no member or parameter named for promotion or the reserve, and a reserve ticket that is not Approved is never touched. *`PreOpenArchitectureTests`, `PreOpenCheckTests`.*
4. Only Approved tickets are moved; Proposed, Modified, Lapsed, Rejected and Reserve-Proposed are left. *`PreOpenCheckTests`.*
5. The database refuses arming a ticket that is not approved (proposed, modified, rejected, lapsed). *`PreOpenArmingDatabaseTests`.*
6. An armed instrument enters the provider's set and leaves at expiry. *`PreOpenArmingDatabaseTests`, `ArmedTicketsProviderTests`.*
7. Cap: more than 50 are refused with the reason, the first 50 stay armed; the feed cap cuts the armed ticket first and displaces no other member. *`PreOpenCheckTests`, `ArmedTicketsProviderTests`.*
8. With the service paused or no armed ticket the feed's set is exactly what it was. *`ArmedTicketsProviderTests`, `PreOpenArmingDatabaseTests`.*
9. Idempotent second run (no new rows). *`PreOpenCheckTests`, `PreOpenArmingDatabaseTests`.*
10. The wording is pinned; the services start paused at 09:10 and 15:40 and raise no schedule issue. *`PreOpenRulesTests`, `PreOpenServiceTests`, `ServicesApiTests`.*
11. After 09:15 nothing is armed. *`PreOpenCheckTests`.*
12. Architecture rules, no-order tripwire, log id uniqueness, `scripts/check.sh` green. No API change (no `gen-api`).

## Contract
- **API:** none new. The two services appear on the Services screen (`GET /api/services`).
- **Data:** none new. State rows in `trading.plan_events` (`system:preopen`, `system:expiry`); inbox rows in `workflow.suggestions` (kind `ticket_invalidated_preopen`).
- **Events/jobs:** `preopen` 09:10, `plan-expiry` 15:40, both paused until switched on; `NOTIFY active_set_changed` after arming and expiry.

## Non-functional
Pre-open finishes in one quote request (at most 50 symbols) plus one day-bar read each, well before 09:15. Logs carry ids, counts and codes only (4780 to 4787), never a price, symbol or reason. Time through `TimeProvider`.

## Test plan
Domain tables (`PreOpenRulesTests`), application over fakes (`PreOpenCheckTests`, `ArmedTicketsProviderTests`, `PreOpenServiceTests`), infrastructure (`PreOpenSourceTests`), integration on a real database (`PreOpenArmingDatabaseTests`, `ServicesApiTests`), architecture (`PreOpenArchitectureTests`, existing no-order and log-id tests).

## Safety-review changes (second commit, 2026-10-24)
- **Stale quote (M1).** The handler refuses before 09:08 IST (`plans.preopen_early`; after 09:15 `plans.preopen_late`). The same existing `/v2/marketfeed/quote` answer is now parsed for `last_trade_time` (read as `dd/MM/yyyy HH:mm:ss` India time or epoch seconds; any other form is null) and `volume`, carried on `SecurityQuote`. A price with no trade time, or one not from today (India date) and inside 09:00 to 09:15, reads `unavailable_quote` (fail closed). Batch guard: with 3 or more readings and at least 80 % having last exactly equal to the previous close, those tickets are invalidated `stale_quote` and the summary says "Price equals previous close for N of M: probably no pre-open price yet." One zero-gap ticket in a batch that is not stale still arms. Every arming reason and the run summary carry "Last price may be the previous close before the open." **No new endpoint, field request, credential or allow-list entry.**
- **Precondition before switching `preopen` on (owner/operator task, NOT done):** capture one real quote answer at about 09:10 on a trading day and record what `last_trade_time`, `last_price` and `ohlc.close` look like (the time format, and whether the last price is the pre-open price). Until then the format of `last_trade_time` is an assumption; an unreadable one invalidates everything, which is safe but useless. Also a follow-up: record the result in this spec and drop the caveat sentence.
- **Wording (M2).** Arming reasons, the service descriptions, the run summary and the inbox text say "Price alerts on trigger and stop are not built yet; only the live price is shown." The pinned `Armed` sentence is unchanged.
- **Expiry (M3).** If any ticket move of a plan fails, the plan is not moved to Expired and is not counted as closed; the next run retries.
- **M4.** The arming reason says "Ban list, surveillance and price band were not re-checked."; the `active_set_changed` NOTIFY (both handlers) and the inbox write are non-fatal (logged, ids 4788 and 4789) and the loop goes on.
- **Minors.** The ATR counts only when the newest complete day bar is the previous trading session (trading calendar); a failed quote read short-circuits the bar reads; the database tests also forge an armed row claiming `from_state = approved` over a non-approved ticket (`fk_plan_events_prev`).

## Honest limits
- Not verified against a real 09:10 morning: Dhan's quote may carry the previous close as the last price before the open (then the gap reads 0 and the rule passes). A pre-open price from the exchange's call auction is not read.
- The trigger/stop levels are text, so the gap is not compared with the trigger and no price-crossing alert exists. The armed instrument is only watched.
- Ban list, surveillance and price band are not re-checked (no source).
- The ATR is a plain 14-session mean of unadjusted daily bars (a split inside the window distorts it).
- The cap of 50 and the gap constant 0.5 are code constants, not owner settings.
- Reserve tickets are armed only if the owner approved them by hand (P7-10); nothing promotes them.

## Open questions
None blocking. Follow-ups: numeric trigger/stop on the ticket and a price-crossing alert with a notification port; a settings home for `gap_invalidate_atr` and the cap.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] No contract change; no migration; ADR 0096; docs and roadmap updated
