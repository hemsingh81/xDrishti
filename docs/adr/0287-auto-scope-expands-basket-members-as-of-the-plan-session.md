# 0287 — Auto scope expands basket members as of the plan session

- **Status:** Accepted (owner, 2026-11-13, REVIEW-2026-11-10 question 3, recommended option).
- **Date:** 2026-11-13.
- **Relation to other ADRs:** Builds on [0251](0251-auto-scope-is-an-insert-only-id-list-that-filters-candidates-before-selection-and-suggestions-are-a-read-model-over-the-stored-plan.md) (the scope) and 0254 item 6 (system baskets know their membership only from the first fetch). Spec: AU-16.

## Context
`AutoScopeResolver` expanded the scope's baskets to their members now. A derived basket version effective on the next session was therefore not used by the plan for that session, and a member removed or added between the run and the session was judged on today's list.

## Decision
1. `IAutoScopeSource.ResolveAsync(asOf)` is the port; the selection plan builder passes the plan's date (`request.ForDate`, the session the tickets are for). Members are those valid at the close of that date (`valid_from`/`valid_to`); an archived basket at that date adds none. `null` stays the live read.
2. A system basket asked for a date before its `membersKnownSince` gets the first known members (ADR 0254 item 6). That is not proof, so the resolver returns the basket in `ResolvedAutoScope.BasketsBeforeKnownSince` and the builder logs `plan.scope_members_before_known_since` (warning, count only). Unknown membership is never treated as a pass: the first known members stand in and the limit is visible.
3. SR-01 (question 17) needs no change: the replay already reads each basket's members per replayed session through `ReplayFacts`, which carries the same known-since flag as survivorship.

## Consequences
- The nightly Auto plan for session D uses the members valid on D.
- Known limit, not built: the scope definition itself (which baskets and instruments) is not versioned; the latest scope applies to any date.
- The flag is logged only; storing it on the plan record is a later step if the owner wants it shown.
