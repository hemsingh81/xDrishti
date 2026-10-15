# P2-16 — Self-healing sync and actionable notifications

- **Status:** Approved (owner, 2026-10-15: "Approved previous request"; decisions: pop-up with action plus bell; retry quietly and tell only if it persists; all three parts) · decision [ADR 0027](../adr/0027-self-healing-sync-and-actionable-notifications.md) · needs [P2-13](P2-13-dhan-trade-history-and-ledger-sync.md) and [P2-14](P2-14-dividends-and-charges-in-returns.md) built
- **Phase / roadmap row:** P2 — makes the account sync (holdings, trade history, ledger) run unattended; polishes the notification system (P1-21, P2-09)
- **Design refs:** docs/design/13-reports-and-ui.md (notification bell, toasts), 12-portfolio-monitoring.md §3 ("Dhan sync (P2-13)"), 04-accounts-access-and-operations.md · [ADR 0018](../adr/0018-services-framework-and-editable-schedules.md) (retries, catch-up) · [ADR 0022](../adr/0022-action-center-and-price-alerts.md) (notices) · [ADR 0025](../adr/0025-dhan-trade-history-and-ledger-sync.md)

## Goal
The owner does not babysit syncs. What can be fixed by itself is fixed by itself (retries, catching up, reading deeper history); what only the owner can do arrives as one **actionable notification** — a coloured pop-up while the app is open and an item in the bell — with a button that goes straight to the page or action that fixes it, and disappears by itself once the cause is gone.

## What the owner's tradebook showed (analysis in `data/dhan/tradebook/ANALYSIS.md`, 2026-10-15)
The exported file (a multi-year export in which cash, derivative and commodity rows are mixed) has **no side, symbol, ISIN or trade id**, names vary over time and the quantity traded differs from what is held (sells). It cannot be imported safely. Trades before the account's first-import date (default 2025-01-01) are not read by the sync today, so the app's history is shallower than the owner's. Hence this story's *deepen* rule (criterion 4): the app reads earlier history by itself when the trades it has do not explain a holding.

## Decisions taken by the owner (2026-10-15)
1. **Pop-up with action and bell** for anything that needs the owner, once per item.
2. **Retry quietly, tell if it persists**; notify at once only when nobody but the owner can fix it (e.g. expired Dhan login).
3. **All three parts** in one story, built in this order: heal rules → new actionable items → pop-up restyle.

## Scope
**In:**
- **Heal rules** (criteria 1–6) for the holdings refresh, the quotes, the Dhan trade/ledger runs and the lot rebuild, with retry back-off and a persistent-failure counter per account and kind.
- **Actionable items** (criteria 7–10): server-derived `attention` items (id, severity, title, text, action: label + route/command, `since`, `resolved`) added to the existing `GET /api/portfolio/notices` family and shown in the bell; items disappear when their condition clears.
- **Pop-ups** (criteria 11–14): a restyled, stacking toast with severity colour, icon, title, message and action buttons; new attention items pop up once.

**Out (not in this slice):** importing the owner's tradebook file (no side/ISIN — see above; the Dhan history read replaces it; a completeness check of the synced trades against the local analysis in `data/dhan/tradebook/` (not committed) is a follow-up script); e-mail/push channels; per-item snooze; changing the Dhan login flow itself (the owner must still log in at Dhan); alert sounds.

## Acceptance criteria
**Self-heal rules (backend; each with a test that proves the "persist" threshold)**
1. **Transient failures retry quietly.** A holdings refresh, quote poll, trade read or ledger read that fails with a transient reason (timeout, 429, 5xx, network) is retried automatically with back-off (1, 5, 15 minutes; at most 3 retries per occurrence, inside the day) and recorded as one run with `attempts`. Nothing is shown to the owner while retries remain. A permanent reason (rejected input, `portfolio.history_too_large` after halving to one day) is not retried.
2. **Persistence threshold.** Per account and kind, `consecutiveFailedDays` counts days whose last attempt failed; it resets on a success. An item (criterion 7) is raised at **2** consecutive failed days, or at once for reasons only the owner can fix (expired or missing Dhan login, API key expired, role switched off).
3. **Catch-up after reconnect.** When an account is reconnected (token renewed), the holdings sync and the trade/ledger reads that were missed run by themselves within a minute, in that order, without a button press; the item "Reconnect Dhan" resolves when the token is valid.
4. **Deepen history by itself.** When a holding's rebuild result is `quantity_mismatch` because the trades explain more or less than is held in a way an earlier read could fix (`negative_opening`, `sell_exceeds_open`), the nightly run reads one earlier year of trades per night (stopping at Dhan's first empty/refused year or 8 years back, whichever first), re-runs the rebuild for the affected holdings, and records `deepenedTo`. A holding still unexplained after the last reachable year raises the item in criterion 9.
5. **Interrupted and stuck runs heal.** A run left `running` past its limit is failed and, if transient, retried (extends the existing recovery); a stuck lock never needs a restart.
5a. **Windows that dropped rows are read again after an update.** A trade or ledger window that dropped rows for a data reason (e.g. no symbol, unmapped product) records `needsReread`. The reader carries a `MapperVersion` constant; when the stored version of an account's sync state is older than the current one, every window with `needsReread` is read again on the next run (identity dedupe makes it safe) and the version is stored. A run can therefore never leave dropped rows behind for good: observed when a first real read dropped nearly all rows because of a mapping gap that was fixed the same day.
6. **Self-resolving.** Every condition that raised an item is re-evaluated on each relevant run and on read; when it no longer holds the item is `resolved` (kept for 24 h in the drawer under "Resolved", then dropped) and its pop-up closes if open.

**Actionable items (server-derived, one place)**
7. `GET /api/portfolio/notices` (and the bell) additionally carry these items, each with a stable id that includes the situation (so a changed situation is a new, unread item): 
   - **Reconnect Dhan** (token expired/missing/expiring) → action opens the account's Connection tab *(exists in the bell; unify the shape)*.
   - **A sync keeps failing** ("Trades have not been read for 2 days: <plain reason>") → action *Retry now* (runs the sync) and *Open*.
   - **Holdings need review** (`needs_review`: hand-dated lots) → action opens the first such holding's rebuild preview; text says how many.
   - **Trades don't explain a holding** (still `quantity_mismatch` after deepening) → action opens the holding's Trades section; text names up to 3 symbols.
   - **Possible dividends in the ledger** (pending suggestions) → action opens the Cash & dividends panel on the suggestions.
   - **Unmatched securities** (trades or holdings with no instrument-master match) → action opens the list; the master refresh already runs by itself first.
8. Severity: `error` for things that stop data (login, repeated failures), `warning` for decisions affecting figures (review, mismatch), `info` for suggestions. Order: error, warning, info, then newest first.
9. Each item carries at most two actions: a primary route/command and an optional *Retry now*; commands are the existing endpoints (no new mutation surface), executed with the usual CSRF and re-entry rules.
10. Read state stays per browser (existing); an item that changes (new id) is unread again; resolved items are not counted in the badge.

**Pop-ups (frontend)**
11. A **toast queue** (max 3 visible, newest at the bottom right on `md+`, full-width at the bottom on phones, above the bottom safe area) replaces the single snackbar; same `useToast().notify(...)` API plus optional `title`, up to two `actions`, `persistent`.
12. **Look:** a rounded card with a coloured left accent bar and tinted surface per severity (success green, info indigo, warning amber, error rose) using the theme tokens in light and dark mode, a 36 px icon chip (existing hues), title in semibold, message in secondary text, action buttons as text buttons in the severity colour, a close button, and a thin countdown bar for timed toasts (hidden under `prefers-reduced-motion`; the toast then simply stays its full duration). Contrast ≥ 4.5:1 for text in both modes.
13. **Behaviour:** timed toasts: 5 s (plain), 10 s (with action); `persistent` (used for attention items of severity `error`) stay until closed, the action is done or the item resolves; hover/focus pauses; Escape closes the top one; announced as `alert` (error/warning) or `status` (others); focus rules of the current provider (keyboard-started actions move focus to the action) are kept; Ctrl/Cmd+Z undo behaviour of the current toasts is kept.
14. **Attention pop-ups:** the first time a given item id appears while the app is open (and it is unread), one pop-up shows with the item's actions; clicking an action marks the item read and navigates/executes; it is not shown again on reload (remembered with the read state). On first load after login only the highest-severity unread item pops up (the rest wait in the bell) so the owner is not flooded.

## Contract
- **API:** extends `GET /api/portfolio/notices` items with `severity`, `title`, `actions[{label, kind: route|command, target}]`, `since`, `resolved`; account sync status gains `attempts`, `consecutiveFailedDays`, `deepenedTo`. Regenerated with `scripts/gen-api.sh`.
- **Data:** `portfolio.broker_sync_state` gains `consecutive_failed_days`, `last_failure_reason`, `deepened_to` (migration `AddSyncHealCounters`); run counts gain `attempts`.
- **UI:** `shared/ui/ToastProvider` (+ `Toast` card), `features/notifications` (drawer shows actions and a Resolved group), pages' deep links (`?holding=…`, `?cash=suggestions`, Accounts → Connection).
- **Jobs:** the heal logic runs inside the existing P2-13 service and the holdings refresh runner; no new service.

## Non-functional
Retries never overlap with scheduled runs of the same account (existing advisory lock). The notices read stays set-based (no per-account N+1). No secrets, tokens, narrations or amounts in notices or logs (counts and symbols only).

## Test plan
Backend unit: retry/back-off schedule, persistence counters, deepen state machine (stops at empty year / limit), item derivation (each rule, resolution, id changes). Integration: notices endpoint, catch-up after reconnect (fake Dhan), migration. Frontend: toast queue (stacking, persistent, pause, Escape, reduced motion, dark mode via theme tokens), attention pop-up once per id, drawer actions and Resolved group, deep links; visual check in the browser at 360 px, tablet, desktop, 4K and in dark mode.

## Open questions
1. **Retry numbers** (1/5/15 minutes, 3 retries, notify at 2 failed days, deepen one year per night up to 8 years) are proposals; say if you want other values (they will be settings, not constants, if you prefer).
2. **Dhan history depth is undocumented**; the deepen rule stops at the first refused or empty year, so if Dhan keeps only a few years the app will say how far it reached.

## Follow-ups from the review (minor, not blocking)
Found by the independent review of P2-16/P2-17 (2026-10-15); none changes the acceptance criteria, each is a small story of its own.
- **`GET /api/portfolio/notices` writes attention rows** (amendment on criterion 6), which is impure for a query (CQS). Reconcile the items in the healer tick instead and make the GET read-only; today two open tabs can make an item flicker between resolved and raised. (closed in P2-18, N1)
- **`attention_items` has no retention.** It grows by about two rows a day; the runtime role has no DELETE, so it needs either a documented DELETE exception for resolved rows older than a limit or a clean-up job. (closed in P2-18, N2)
- **Transient versus permanent is decided by matching the error's message text** (`SyncFailures`). Carry a structured failure kind from the Dhan client instead, so a reworded message cannot turn a retry into a permanent failure. (closed in P2-18, F1)
- **Quote items:** review how the quote poll's failures appear as attention items (they are recorded against the primary Data account).
- **Mapper version and deepening:** the mapper-version fallback (an account never stamped gets the dates of its earlier runs that dropped rows) and the deepening notes ("rarely changes a verdict") are documented limits; a reconciliation against the broker's realised-P&L statements would show whether either matters.
- **Recurring items are keyed on id plus `since`** (only for the six server attention kinds; every other notice - price alerts, 'holdings need a decision', the API-key expiry - uses the plain id, and the server sends `Since = null` for 'holdings need a decision', whose `At` still moves on each poll: cosmetic) (fixed in this pass; ADR 0027 amendment j): a condition that clears and returns is unread again and pops up again.
- **The compose empty-value semantics of `XD_PORTFOLIO_HEAL_BACKOFF_MINUTES`** (fixed with the `-` form in the compose file: an empty value now means the default).
- **Command targets on the client are a hand-copied fixture** (`shared/api/commands.ts` against the endpoints the server names): they can drift; share one source of truth (for example generate the list from the contract). (closed in P2-18, N4)
- **A ledger-only run blocks the trade Undo and Rebuild** (`SyncGuard` does not tell the kind of run): make the guard kind-aware, or word the message 'can take a few minutes'. (closed in P2-18, F3)
- **A run that fails late re-reads the outside windows on each retry** when noted windows lie inside the run's own range (the mapper version is then stamped only after the run's last window). (closed in P2-18, F4)
- **The phrase lists differ** (`DepositoryCharges`, `LedgerClassifier`, `LedgerCosts`): align them or drop the dead phrases. (closed in P2-18, I1)
- **`BillPlanner` identity is relative to the siblings in the import**, not to those that are not pending (covered by Dhan or skipped). (closed in P2-18, I3)
- **The supersede 'pool' decision** (depository-labelled entries only, else all of the day's charges) is made from the entries newly stored in the save, while `DpPlanner` uses the whole stored day; align them. (closed in P2-18, I2)
- **The DP date assumption** (the charge is booked on the sheet's day) and Dhan's labelling are unverified against a real ledger.
- **Related, from P2-17 (fixed in the same pass; see [P2-17](P2-17-dhan-report-imports.md)):** an import is refused while a sync runs (`409 portfolio.sync_running`; "a sync" is the holdings sync and any broker history run of the account), supersede is computed from the whole stored group, and the command allow-list of the notice actions was tightened (a POST to one of three whole-path patterns: `/api/accounts/{guid}/sync/broker-history`, `/api/accounts/{guid}/holdings/sync`, `/api/services/{id}/run`; a holdings *Retry now* cannot confirm a mass close - it answers a message pointing to *Sync now*).

## Done when
- [ ] All acceptance criteria have evidence
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors (third pass, 2026-10-15)
- [x] Contract regenerated (`scripts/gen-api.sh`); migration added
- [x] Design docs/ADR 0027 updated (doc 13, 12, 04; also 05, 15, 16, 18)
- [ ] Owner checks the pop-ups and one real failure-and-recovery in the running app

## Amendments (2026-10-15)
What the build did differently from the text above, found while building and reviewing; [ADR 0027](../adr/0027-self-healing-sync-and-actionable-notifications.md) lists the same under "Amendments (2026-10-15)". The design is in [doc 12](../design/12-portfolio-monitoring.md) §3 "Self-healing and attention items (P2-16)" and [doc 13](../design/13-reports-and-ui.md) "Notifications and pop-ups (P2-16)".
- **Health kinds are added to `portfolio.broker_sync_state`.** The table holds a row per account and kind `holdings | trades | ledger | quotes` (migration `AddSyncHealCounters`), `watermark_date` is nullable; it also gains `attempts`, `last_failed_at`, `deepened_on`, `deepen_exhausted` and `mapper_version` beyond the three columns named in *Contract*, and two tables are new: `portfolio.broker_sync_windows` (criterion 5a) and `portfolio.attention_items` (the memory of the items, criteria 6 and 10).
- **The price poll retries by being a poll (criterion 1).** The 5-minute poll of `holdings-quotes` is its own retry; a transient failure is recorded as a skipped run and counted against the Data account, and the run **fails quietly until two days** in a row (then the failed-service notice and the item); a refused token fails it at once.
- **Reconnect notices (criteria 3 and 7).** The frontend-derived notices for a missing, expired or expiring Dhan login are replaced by the server's `reconnect_dhan` item (for every active account); `key_expiring` and `key_expired` stay in the frontend.
- **`GET /api/portfolio/notices` writes (criterion 6).** Every read reconciles the remembered items (three statements); resolved items are kept 24 hours and never deleted. *Changed by P2-18 ([ADR 0030](../adr/0030-read-only-notices-retention-and-structured-failures.md)): the read only reads, the healer tick and the end of every sync run reconcile, and resolved items are deleted after 30 days.*
- **`role_off` and the API-key expiry are not recorded server-side (criterion 2).** The code `role_off` exists and counts as owner-only, but only accounts with the role on are looked at, so nothing raises it in practice; "API key expired at once" stays the browser's notice. The Dhan login (`token_expired`, `token_refused`, `not_connected`) is the owner-only reason that is told at once.
- **Deepening can rarely change a verdict (criterion 4).** An earlier year enters the same net as the rest (the opening quantity is what remains after the trades), so it helps when a sale needs buys from before the dates read, not for transfers, allotments or demergers. Deepening runs only after a scheduled trades run (the 16:30 service, a quiet retry or a catch-up), at most once per India day, and the item "Trades do not explain a holding" waits for the deepening to be over only for the two deepenable details (`negative_opening`, `sell_exceeds_open`); any other mismatch raises it at once.
- **Healing needs the refresh runner enabled.** `Portfolio:AutoRefresh:Enabled = false` also switches the quiet retries and the catch-up off ("Retry now" and the schedules keep working). Settings: `Portfolio:Heal:RetryBackoffMinutes`, `PersistDays`, `DeepenMaxYears` (open question 1: they are settings, compose names `XD_PORTFOLIO_HEAL_BACKOFF_MINUTES`, `XD_PORTFOLIO_HEAL_PERSIST_DAYS`, `XD_PORTFOLIO_HEAL_DEEPEN_YEARS`).
- **A retry never crosses midnight; the unmatched item looks at 90 days.** A transient failure is retried at most three times in its India day; the unmatched-securities item counts open holdings with no instrument-master match and securities of the trades read from Dhan in the last 90 days with none, and its link lists holdings only.
- **Still open from the *Done when* list:** the evidence for every criterion, `scripts/check.sh` with the independent reviewer pass and the owner's check of the pop-ups and one real failure-and-recovery.
- **Recurring items are keyed on `id` plus `since` in the browser** (only for the six server attention kinds; every other notice - price alerts, 'holdings need a decision', the API-key expiry - uses the plain id, and the server sends `Since = null` for 'holdings need a decision', whose `At` still moves on each poll: cosmetic) (criteria 10 and 14): the server re-raises a resolved condition under the same id with a new `since`, and the read set and the pop-up memory treat that as a new item ([ADR 0027](../adr/0027-self-healing-sync-and-actionable-notifications.md) amendment j).
