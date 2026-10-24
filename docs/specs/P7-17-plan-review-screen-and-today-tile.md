# P7-17 — Plan review screen (minimal) and the Today plan tile

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22 and the owner's binding decisions of 2026-10-23; every question taken as the recommended answer, Q11 included). Built 2026-10-24.
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, slice P7-17 of [P7-00](P7-00-selection-accounts-and-nightly-plan.md), 4 d
- **Design refs:** docs/design/10-trade-selection.md §6b, §7 · 13 (UI: ticket cards, bulk approve, countdown to cutoff) · ADR 0095 (review endpoints), 0092 (basis flags, wiring), 0090/0093/0094 (grade, journal, digest)

## Assumptions (taken as recommended; the owner's decisions are binding)
1. **Q11: the minimal review screen belongs in P7.** Everything else of the Plan screens stays P8.
2. **No order, ever.** The screen says so at the top, in the plan header, on every ticket's action area, in every dialog and in every approval answer, using the server's pinned sentence "Approved for watching: no order is placed and none exists." Nothing on the screen can place an order and no endpoint for one exists.
3. **Paper accounts only.** The mode of every allocation is shown on every ticket; the screen never offers a live account.
4. **Manual approval.** The automatic-approval setting is shown read only (from `GET /api/autonomy/settings`); the screen has no control for it and never calls the autonomy PUT. Reserve tickets are never promoted; approve-all says it does not include them.
5. **Route `/plans`** (the planned nav entry `/review` and the doc-13 working name `/plan-review` are replaced by `/plans`, "Plan review", no longer a "coming in phase" item). The Today tile is added to the existing Today page.
6. **Plan basis flags are read from the plan.** The API exposes `provisional` and the free-text `basis`; "market gate unavailable" and "Open book unavailable" are recognised from the wording the pipeline writes (ADR 0092) and shown as chips; the full basis text is always shown too. A structured flags field would be better: follow-up.
7. **Per-account standings come from `GET .../review`**; the codes to confirm are the checks with standing `needsconfirmation` (not the flat `needsConfirmation` list), so the dialog names exact codes per account. A check with standing `refused` (the account gate) hides "Confirm despite".
8. **One reason for all accounts** in a confirm-despite (1 to 200 characters, trimmed); the request holds one `{accountId, codes, reason}` per account that has codes to confirm. The server's `ignored` list is shown after the answer.
9. **No optimistic update.** The screen changes only from the server's answer: after every decision (success or error) every plan query is read again.
10. **Today tile shows the newest plan** (the list is newest first), with state, counts, flags and the cutoff time in IST; no live countdown on the tile (the countdown is on the screen).
11. Digest list uses the existing `GET /api/learning/digests`; grade and journal panels are read only with their basis wording from the server.
12. Only the review-screen data exists in the API as listed; **follow-ups (not invented):** a structured basis-flags field on the plan; the smart-rule/gate banner detail and the "plan not published (reason)" banner (needs `GET /api/plan-runs` wiring, which exists but is not used here); charts per ticket; accept/snooze of suggestions; per-account (not plan-wide) standings; a notification before the cutoff.

## Goal
The owner can open Plan review, read the evening's plan with its evidence and honest limits, approve (or confirm despite named checks), lower or reject each primary ticket, approve all clean ones, see what lapses at the cutoff, and read grade, journal, suggestions and digests afterwards, with nothing suggesting that an order is placed.

## Scope
**In:** feature `frontend/src/features/plans` (api, hooks, components, lib), lazy route `/plans`, nav entry, `PlanTile` on Today, default MSW handler `GET /api/plans`, fixtures `src/test/planFixtures.ts`, tests, docs, roadmap row. Frontend and docs only: no backend change, contract unchanged (the generated types already hold every shape used).
**Out:** the rest of P8 screens, charts, accept/snooze, any change to settings (including the auto option), any order, reserve promotion.

## Acceptance criteria
1. Empty list: "No plan yet"; error: the problem title; loading: a status indicator. Same for the tile. *`PlansPage.test.tsx`.*
2. The list shows each plan's state as text and the flags provisional, market gate unavailable, open book unavailable. *test.*
3. The detail shows primary, reserve and rejected groups; each ticket's evidence (cell, trades, data range with its limits), unavailable inputs, reasons, sizes with the mode, the checklist standings per account; the cutoff with a live countdown and "Undecided tickets lapse at the cutoff."; the auto-approval state read only. *test, `Countdown.test.tsx`.*
4. Approve is disabled with the reason visible when a check needs confirmation, the checklist is missing, the ticket is decided or decisions are closed. *test.*
5. Confirm despite lists the exact codes per account, requires a reason, sends exactly `{accountId, codes, reason}` per account, and shows the server's `ignored` list. *test.*
6. Modify (size down only, validated in the form), Reject (reason), Approve all (dialog states the exact count and that reserve tickets are not included, then lists skipped tickets). *test.*
7. Every action area and dialog carries the pinned no-order wording; approval answers show the server's notice. *test.*
8. 401, 403 and 409 `plans.closed` / `plans.stale` are shown in words; the screen is updated from the server, never optimistically. *test.*
9. Suggestions inbox with "Mark seen"; grade labelled "simulated on plan prices"; journal labelled "realised fills, not simulated"; digests read only. *test.*
10. Server text is rendered as plain text (a test feeds markup and finds no element). *test.*
11. Today shows the plan tile (state, counts, cutoff, no-order line). *test.* `scripts/check.sh` green.

## Contract
- **API used (all existing):** `GET /api/plans`, `/api/plans/{id}`, `/review`, `/grade`, `/journal`, `/suggestions`; `POST .../items/{itemId}/approve|modify|reject`, `.../approve-all`, `.../suggestions/{id}/seen`; `GET /api/learning/digests`, `/api/autonomy/settings`, `/api/accounts` (names only).
- **UI:** `/plans` (lazy), nav "Decisions / Plan review"; Today tile. Phone-usable: single column below the `md` breakpoint.

## Non-functional
Route-level lazy; no new dependency; no secrets or reasons in logs (the screen logs nothing). Accessibility: states as text, labelled controls, keyboard-operable modal dialogs, no `dangerouslySetInnerHTML`.

## Test plan
Vitest + Testing Library + MSW as listed; the clock pinned (`pinClock`).

## Honest limits
- Flags are recognised from the basis wording. With the checks as wired today (data quality never available) every real ticket needs a confirm-despite or lapses; the screen shows exactly that.
- Looked at only through tests; not seen in a browser (login required).

## Open questions
None.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Contract unchanged; roadmap updated
