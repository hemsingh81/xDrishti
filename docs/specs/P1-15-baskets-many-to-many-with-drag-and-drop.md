# P1-15 — Baskets: group tracked instruments (many-to-many), arrange with drag & drop

- **Status:** Approved (owner, 2026-10-11: "arrange those in the basket and one instrument can be part of multiple baskets"; answer: manual baskets + drag & drop)
- **Phase / roadmap row:** P1 — Data platform & instruments · stories **P1-15** + the Baskets half of **P1-17** · depends on [P1-05](P1-05-tracked-instruments.md)
- **Design refs:** docs/design/06 §3, §5; prototype `docs/prototype/` → Baskets

## Goal
The owner creates baskets, adds instruments (tracked or from the master list), arranges them — reorder inside a basket, drag between baskets to copy or move — and every change is dated. One instrument can sit in many baskets.

## Scope
**In:** manual baskets only (name, colour, purpose, description), membership with history (valid_from / valid_to), ordering, copy/move, Baskets page. **Out:** rule-based and system baskets, target weights, risk overrides, scan/lab scoping (later stories; P3-09 consumes baskets).
Note: portfolio **buckets** (Trading / MyLongTerm / MyChoice, P1-16) are a different concept (one bucket per holding for additive portfolio totals) and stay separate.

## Design (defaults — object to any)
1. **Tables (`catalog` schema):** `baskets` (id, name unique among active, colour, purpose `trading|long_term|watch`, description, position, created_at, archived_at); `basket_members` (id, basket_id, instrument_id, position, valid_from, valid_to nullable, note, added_by) with a unique index on (basket_id, instrument_id) where valid_to is null — **an instrument can be in many baskets, once per basket at a time**; removing sets `valid_to` (history kept), re-adding creates a new row.
2. **Seed:** the three baskets named in the design — **MyChoice** (trading), **MyLongTerm** (long_term), **Best stocks** (trading) — created empty by the migration (idempotent); the owner can rename/archive them.
3. **Rules:** adding an untracked instrument **tracks it automatically** (as in the prototype; P1-05 semantics); removing the last basket membership does not untrack; archived baskets are hidden but their history stays; max 2,000 members per basket.
4. **API (session, no step-up):** `GET/POST /api/baskets`, `PUT /api/baskets/{id}`, `DELETE /api/baskets/{id}` (archive), `PUT /api/baskets/order {ids[]}`; `GET /api/baskets/{id}/members?asOf=` (current or as-of date) ; `POST /api/baskets/{id}/members {instrumentIds[], note?}` (idempotent add), `DELETE /api/baskets/{id}/members {instrumentIds[]}`, `PUT /api/baskets/{id}/members/order {instrumentIds[]}`, `POST /api/baskets/transfer {fromBasketId, toBasketId, instrumentIds[], mode: copy|move}` (atomic), `GET /api/instruments/{id}/baskets`; audit events for create/rename/archive/add/remove/transfer with counts; every error as problem details.
5. **UI (Baskets page):** left column of basket cards (colour dot, name, purpose chip, member count; reorderable; "New basket"); right panel with the selected basket's members grid (symbol, name, type, sector, tracked status, added date, remove), **drag handle to reorder**, **drag a row (or a multi-selection) onto another basket card to copy; hold Alt/Option to move** (drop target highlights, toast with Undo), plus an accessible **"Copy / Move to…" menu** and keyboard drag (arrow keys + space via `@dnd-kit`, exact version pinned) so everything works without a mouse; **Add instruments** dialog (search master/tracked with multi-select, already-in-basket marked); basket editor dialog (name, colour, purpose, description); history drawer (added/removed dates, notes); instrument drawer shows its baskets; empty states ("This basket is empty — add instruments"); loading/error; 360 px → 4K (cards become a select/tab strip on phones).

## Acceptance criteria
1. An instrument can be in many baskets, never twice in one active basket (DB-enforced); remove/re-add keeps dated history; `asOf` returns the membership as it was; archive hides a basket and keeps history.
2. Add auto-tracks instruments (P1-05) in the same transaction; adding retired instruments is refused with a clear reason.
3. Reorder (baskets and members) and transfer (copy/move) are atomic and idempotent; concurrent edits do not corrupt order (optimistic concurrency or deterministic reordering) — covered by tests.
4. Drag & drop works with mouse, touch and keyboard, with the menu alternative; announcements for assistive tech; Undo after a move/copy.
5. Tests: domain/application, integration (many-to-many, history, as-of, transfer, concurrency, grants), frontend (MSW: create/edit/archive, add dialog, reorder, copy/move via DnD and via menu, history), dnd keyboard test; `scripts/gen-api.sh`, `scripts/check.sh` green; reviewer no blockers/majors; deployed and verified in the browser; docs (06 §3/§5) updated; bundle ≤ 200 KB gzip initial (Baskets is a lazy route).

## Effort
5 pts. **Done when:** [ ] criteria evidenced · [ ] check.sh green + reviewer · [ ] contract regenerated, migration · [ ] deployed and verified
