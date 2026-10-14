# 0027 — Syncs heal themselves; what only the owner can do arrives as one actionable notification

- **Status:** Accepted (owner, 2026-10-15: "Approved previous request" on [P2-16](../specs/P2-16-self-healing-sync-and-actionable-notifications.md)) — builds on [ADR 0018](0018-services-framework-and-editable-schedules.md) (retries, catch-up), [ADR 0022](0022-action-center-and-price-alerts.md) (notices) and [ADR 0025](0025-dhan-trade-history-and-ledger-sync.md)
- **Date:** 2026-10-15

## Context
The Dhan syncs (holdings, quotes, trades, ledger) now run by themselves, but a failure only shows up if the owner opens the right screen, and the notification bell only knows accounts, data and services. The owner asked that most things heal on their own and that anything needing their action arrives as a notification with a way straight to the fix, with better-looking pop-ups. The owner's trade file (`data/dhan/tradebook/ANALYSIS.md`) also showed that the app's history starts at the first-import date while the owner traded since mid-2023.

## Decisions
1. **Transient failures retry quietly** (1, 5, 15 minutes; at most 3 per occurrence, inside the day); permanent ones do not. Nothing is shown while retries remain.
2. **A failure becomes the owner's business at 2 consecutive failed days**, or at once when only the owner can fix it (expired/missing Dhan login, expired API key, role switched off).
3. **Catch-up after reconnect:** a renewed token triggers the missed holdings, trades and ledger syncs by themselves.
4. **History deepens by itself** when trades do not explain a holding in a way earlier history could fix: one earlier year per night, stopping at Dhan's first refused/empty year or 8 years back.
5. **Items are server-derived and self-resolving.** Notices carry id (includes the situation), severity, title, text, at most two actions (route or existing command) and `resolved`; they disappear when the condition clears. Read state stays per browser.
6. **Pop-ups:** a queue of up to three severity-coloured cards with title, message, actions, close and a countdown (not under reduced motion); errors needing action stay until acted on or resolved; a new item pops up once, and after sign-in only the most severe unread one does.

## Consequences
- Nothing new needs a button to stay current; the owner hears only about what they must do, once, with the button.
- Retries and deepening add Dhan calls at night (bounded by the existing spacing and caps).
- Dhan's history depth is undocumented; the deepening stops where Dhan stops and says how far it reached.
