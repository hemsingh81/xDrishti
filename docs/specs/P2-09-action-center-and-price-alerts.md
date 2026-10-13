# P2-09 — Portfolio Action center and price alerts

- **Status:** Approved (owner, 2026-10-13: "approved as written", after an analysis of the ADR and spec — gaps settled in ADR 0022 'Review before approval' and the amendments to criteria 7, 8, 9, 13, 15, 16 below) · decision [ADR 0022](../adr/0022-action-center-and-price-alerts.md) (Accepted)
- **Phase / roadmap row:** P2 — "Action center, price alerts" · after P2-06 (quotes), P2-07 (benchmark), P2-08 (sectors, baskets)
- **Design refs:** docs/design/15-smart-insights-and-automation.md §3 (Action center), §4 (price alerts), 12-portfolio-monitoring.md §7, §10, 13-reports-and-ui.md (Portfolio)

## Goal
The Portfolio screen tells the owner which holdings need a decision and why (below cost, large gain, overweight, sector concentration, long-term soon, lagging Nifty, not in a basket), lets them act on each (open, set an alert, add to a basket, snooze, done), and notifies them in the bell when a price alert they set is hit.

## Scope
**In:**
- Seven Action-center rules as pure functions over a read model, thresholds with defaults editable in Settings; items with severity, numbers and suggested actions; stored decisions (snooze/done); audit; bell summary notice.
- Price alerts: create/list/cancel, one-shot, checked on every quotes-service tick from the same quote answer (alert instruments join the quoted set), audit, bell notice.
- Frontend: Action center card, Alerts panel/dialog, thresholds card in Settings, bell entries.

**Out (not in this slice):** *Results due* and *Max holding exceeded* rules (no events data / no trading buckets yet), per-account tiles (the Group by Account view covers the figures), indicator or trailing alerts, alerts on instruments outside the NSE master list, email/push channels, snooze durations other than 7 days, per-holding threshold overrides, minute-level alert checking (needs the live feed).

## Acceptance criteria
**Rules (pure, table-driven tests)**
1. *Below cost*: open priced holding with unrealised % ≤ −10 → item `medium`; ≤ −20 → `high`. *Large gain*: unrealised % ≥ +25 → `info`. Unpriced holdings never fire a price-based rule (they are listed once as "no price" in the counts, not as items).
2. *Overweight*: weight of the holding in its account's priced value > max(15 %, 1.5 × (100 / positions) %) and the account has ≥ 3 priced positions → `medium`; weight and threshold are in the item's numbers.
3. *Sector concentration*: one sector > 35 % of an account's priced value (sector from the master; "Unclassified" never fires) → one item per account+sector (`medium`), listing the sector share and its largest holding.
4. *Long-term soon*: an open lot with unrealised gain whose age reaches 12 months within the next 30 days (calendar, India date) → `info`, one item per holding listing the lots and dates; informational only (text says "check the tax impact before selling early", no tax advice).
5. *Lagging Nifty*: holding held > 180 days (oldest open lot) whose XIRR is more than 10 percentage points below the benchmark XIRR of the same cash flows (reuses `BenchmarkReturns`) → `medium`; not evaluated (and counted as `unavailable`) when the benchmark has no bars from the first flow or either XIRR is not `ok`.
6. *Not in a basket*: open holding mapped to the master whose instrument is in no live basket → `info`.
7. Thresholds (below-cost %, high %, gain %, overweight floor % and multiple, min positions, concentration %, lag pp, lag days, long-term window days) come from the stored settings or the documented defaults, within the fixed ranges of ADR 0022 (below-cost 1–90, high 2–95 and > below-cost, gain 1–1000, floor 1–100, multiple 1–10, positions 2–50, concentration 5–100, lag pp 1–100, lag days 30–3650, window 1–365); invalid values are refused with field errors; "Restore defaults" writes the defaults. Changing a threshold changes the next read immediately.
8. Item ids are stable and path-safe: `{rule}:{holdingId}`, or `{rule}:{accountId}:{first 12 hex of SHA-1 of the lower-cased sector}` for concentration (the sector name is in the numbers); an item's severity and numbers are recomputed on each read.

**Decisions**
9. *Snooze* stores `snoozed` until now + 7 days (India date arithmetic); the item reappears afterwards if still true. *Done* stores `done`; the item stays hidden while the condition holds and the stored state becomes `cleared` (an UPDATE — no DELETE exists) **when the condition no longer holds**, so a later recurrence fires again; the reconciliation runs after every quotes tick and holdings sync and opportunistically inside `GET /api/portfolio/actions` (idempotent, the only write a read makes); test: done → condition clears → reconcile → condition returns → the item is open again, including with no tick in between (clear via GET). *Reopen* clears either. States of closed holdings are ignored. All three write an audit event (who, rule, holding/account).
10. `GET /api/portfolio/actions?accountId&bucketId&status=open|snoozed|done|all` (default open) → `{items[{id, rule, severity, accountId, accountName, holdingId|null, instrumentId|null, symbol|null, title, detail, numbers{…}, suggestions[open|set_alert|add_to_basket|snooze|done], state, snoozedUntil}], counts{open, snoozed, done, byRule{…}, bySeverity{…}, unavailable{lagging}}}` sorted by severity then size of the trigger; scope semantics as the summary (accounts counted in totals unless `accountId` is given); read-only; < 300 ms for 500 holdings; one holdings+lots read, one sector read, one basket read, one bars read only for *Lagging Nifty*.
11. `POST /api/portfolio/actions/{id}/snooze|done|reopen` → the item's new state; `404 portfolio.not_found` for an item that is not currently produced by a rule (stale id) except `reopen`; idempotent; session + CSRF.
12. `GET/PUT /api/portfolio/action-thresholds`, `POST …/reset`; settings in `platform.app_settings`; audited.

**Alerts**
13. `POST /api/portfolio/alerts` `{instrumentId, direction at_or_below|at_or_above, level, note?}` creates an `active` alert: instrument must be an NSE master-list **share or ETF** (not retired, not an index), level > 0 and ≤ 10,000,000 with ≤ 4 decimals, note ≤ 200 chars, at most 200 active alerts (`409 portfolio.alert_limit`); when a last known price exists (the holding's last price, else the instrument's last stored daily close; unknown otherwise, then creation is allowed) and the condition already holds it is refused (`400 portfolio.invalid`, field `level`, message says the price). Quick presets ±5/10/20 % are frontend sugar computing a level from the last price.
14. `GET /api/portfolio/alerts?status=active|triggered|cancelled|all` newest first, paged; `POST /api/portfolio/alerts/{id}/cancel` (idempotent; a triggered alert stays triggered). No DELETE anywhere; the app role has no DELETE grant on the table.
15. **Checking:** the condition is evaluated on the tick's last price only (`at_or_below` ⇒ last ≤ level, `at_or_above` ⇒ last ≥ level; a spike between two ticks is not seen, and the screen says so). Each `holdings-quotes` tick quotes the union of open holdings' securities and active alerts' securities (one request set, ≤ 1,000 per call); after the holdings update, every active alert whose condition holds on the quoted last price becomes `triggered` with trigger price and time **in the same transaction as its audit event**; an alert is triggered at most once even with overlapping runs or retries (conditional update `WHERE status='active'`); alert instruments without a quote in the answer stay active; the run message adds "N alerts triggered"; a failed run triggers nothing.
16. Notices (derived on read): `price_alert_triggered` (warn; text with symbol, direction, level, trigger price; id per alert; listed for 7 days after triggering) and `actions_need_decision` (info; "N holdings need a decision" when open items exist; id includes the date so it re-raises daily at most once) in `GET /api/portfolio/notices` (a new endpoint; the bell merges it like the data and service notices, query in `shared/api`), shown in the bell with Open → the Portfolio screen.

**Screen**
17. The Portfolio screen gets an **Action center** card above the History card: count in the header ("5 need a decision"), items grouped by severity (high · medium · info) each with the holding (link to the drawer), the rule title, the numbers in words ("Down 23.4 % from cost (₹1.20 L invested)"), and buttons **Open**, **Set alert**, **Add to basket**, **Snooze 7 days**, **Done** (menu on a phone); a status filter (Open · Snoozed · Done) and the existing scope (account/bucket) applies; collapsed by default when empty ("Nothing needs a decision") and remembered per session; loading, error with Retry, "N rules not evaluated" note when the benchmark is unavailable.
18. **Set alert** opens the alert dialog prefilled with the holding's instrument and last price, direction chips and presets (−5/−10/−20 %, +5/+10/+20 %), a level field with the live "last price" hint and the validation messages of 13; **Add to basket** opens a basket picker (existing baskets API) and adds the instrument (toast with the result, Undo not needed); Snooze/Done update the item at once (optimistic with rollback on error) and toast.
19. An **Alerts** panel (drawer opened from the Action center header and from the holding drawer): tabs Active · Triggered · Cancelled, each row with symbol, condition ("at or below ₹1,200"), note, created, triggered price/time, Cancel; empty states; "checked every 5 min while the market is open" line; create button.
20. Settings gets an **Action center thresholds** card (numeric fields with units and ranges, validation messages, Restore defaults, Save) following the Settings screen's patterns.
21. The bell shows the two notices with Open leading to the Portfolio screen (alert drawer for a triggered alert); read state follows the existing mechanism.
22. Responsive 360 px → 4K, a11y (status never by colour alone, labelled controls, focus management in dialogs/drawers), no new initial-bundle growth beyond ±1 KB (card lazy inside the Portfolio chunk), tests by role/label/text.

## Contract
- **API:** as in 10–14, 16; DTOs in `ApiJsonContext`; `scripts/gen-api.sh`; problem codes `portfolio.invalid`, `portfolio.not_found`, `portfolio.alert_limit`.
- **Data:** migration `AddActionCenterAndPriceAlerts`: `portfolio.action_states(rule text, key text, state text check snoozed|done, snoozed_until date null, updated_by, updated_at, primary key (rule, key))`; `portfolio.price_alerts(id uuid pk, instrument_id uuid, direction text check, level numeric(18,4), note text null, status text check active|triggered|cancelled, created_at, created_by, triggered_at null, trigger_price numeric(18,4) null)` with index on (status, instrument_id); grants SELECT/INSERT/UPDATE, no DELETE; thresholds JSON key in `platform.app_settings`; audit event types added.
- **Quotes service:** `HoldingQuotesService` quotes holdings ∪ active alert instruments and evaluates alerts (port `IPriceAlerts` in Application/Abstractions/Portfolio).
- **UI:** `features/portfolio` (ActionCenterCard, ActionItem, AlertDialog, AlertsDrawer, hooks, lib), `features/settings` thresholds card (via its public index), bell notice derivations in `features/notifications`; shared basket picker through the baskets feature's index or a shared/api port.

## Non-functional
- Rules run in memory over the same reads as the summary/groups (+ sector/basket/bars reads only when a rule needs them); no per-holding queries.
- Alert checking adds no extra Dhan request beyond the larger quote batch; at most 200 active alerts.
- Security: session + CSRF; inputs validated; notes are plain text (rendered escaped); nothing sensitive in logs (counts only).
- Observability: log item counts and alert triggers (ids and symbols only, no prices of holdings).

## Test plan
- Domain/application unit: each rule at/around thresholds incl. unpriced, one-position accounts, Unclassified sector, benchmark unavailable, lot-age edges (India date, leap days); snooze/done lifecycle incl. condition-clears-and-returns; threshold validation; alert creation validation incl. already-true and limit; alert triggering from a quote answer (both directions, equality, missing quote, overlapping runs, transaction with audit).
- Integration (TimescaleDB): actions over real holdings/lots/baskets/sectors; decisions persist; alerts end to end through the quotes service with the fake Dhan (create → tick → triggered → notice → audit); no DELETE grants; query-count guard.
- Frontend (Vitest + MSW): card states, grouping by severity, each button's effect, dialogs (validation, presets), alerts drawer tabs, thresholds card, bell notices, phone layout; no leaked-request guard hits.
- Architecture: rules pure; the quotes service depends on the alert port only.
- Visual check (light, dark, phone), then the owner with real holdings.

## Open questions (owner) — answered 2026-10-13: all as written
1. **Seven rules now**; *Results due* and *Max holding exceeded* later when their data exists — OK?
2. **Evaluate on read** (no evaluation job), only snooze/done stored — OK?
3. **"Done" lasts while the condition holds** (comes back if it clears and recurs); snooze is a fixed 7 days — OK?
4. **Alerts are one-shot and checked on the 5-minute quotes tick** (not every minute until the live feed) — OK?
5. **Thresholds editable in Settings** with the doc-15 defaults; at most 200 active alerts — OK?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddActionCenterAndPriceAlerts`
- [ ] Design docs updated (15 §3–§4, 12 §7/§10, 13), ADR 0022 accepted
- [ ] Deployed; the owner sets an alert on a holding, sees it trigger on a tick, and works through the Action center items
