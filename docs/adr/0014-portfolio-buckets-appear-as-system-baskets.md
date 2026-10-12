# 0014 — Portfolio buckets appear on the Baskets page as read-only system baskets

- **Status:** Accepted (owner, 2026-10-12) — amends the note in [P1-15](../specs/P1-15-baskets-many-to-many-with-drag-and-drop.md) that portfolio buckets "stay separate"
- **Date:** 2026-10-12

## Context
Adding an account with the Portfolio role creates its default portfolio bucket (`"{account} · Portfolio"`, [ADR 0011](0011-default-portfolio-bucket-per-account.md)),
and holdings belong to exactly one bucket. The Baskets page (P1-15) showed only the owner's manual baskets, so the owner could not see the portfolio groups
there and asked for them: "it should show me other default baskets which are created when adding an account … and these should not be allowed to archive".
Design doc 06 §3.1 already names a **System** basket type ("maintained automatically").

## Decision
1. **Portfolio baskets are a view, not copies.** Every portfolio bucket that `GET /api/portfolio-buckets` lists (the default bucket of each account with the
   Portfolio role, and custom buckets) appears on the Baskets page as a **system basket** of kind `portfolio`. Nothing is stored in `market.baskets`: the list
   and the members are read from the buckets and their current holdings, so they can never drift apart.
2. **Members** are the instruments of the bucket's current holdings (quantity > 0), read-only, with the holding's quantity and account shown beside them.
3. **Protected:** a system basket cannot be archived, renamed, recoloured, reordered, emptied or added to — its name follows the account (ADR 0011) and its
   members follow the holdings. The API refuses with `baskets.system` (409); the UI shows no such actions and no drop target.
4. **Usable as a source:** rows can be copied (not moved) from a system basket into a manual basket, by drag and drop or the menu, and the usual auto-tracking applies.
5. **Order and place:** system baskets come first, in a "Portfolio" group above the manual ones, ordered by account then name; they are never part of the manual order.
6. **History** of a system basket is the holdings history, not basket history: no history drawer and no as-of view (the controls are hidden) until the portfolio
   module offers dated holdings.

## Consequences
- Buckets stay a separate concept in the database (one bucket per holding, additive totals); the Baskets page is only a second place to look at them.
- A bucket with no holdings shows as an empty system basket.
- Rule-based and the other system baskets (open positions, today's plan) later reuse the same `kind` and `baskets.system` protection.
