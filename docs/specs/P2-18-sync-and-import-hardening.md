# P2-18 — Hardening of the Dhan sync, the notices and the imports

- **Status:** Approved (owner, 2026-10-15: "start the hardening story now", after the recommendation in the roadmap issues log, ISS-08/ISS-09) · decision [ADR 0030](../adr/0030-read-only-notices-retention-and-structured-failures.md)
- **Phase / roadmap row:** P2 — closes the review follow-ups of P2-13 … P2-17 before Phase 3
- **Design refs:** docs/design/12-portfolio-monitoring.md §3, 13-reports-and-ui.md · roadmap issues log ISS-08, ISS-09 · [ADR 0025](../adr/0025-dhan-trade-history-and-ledger-sync.md), [0027](../adr/0027-self-healing-sync-and-actionable-notifications.md), [0029](../adr/0029-dhan-report-imports-and-source-precedence.md)

## Goal
The sync, the notifications and the imports behave the same on the tenth run as on the first: no query that writes, no tables that grow forever, no behaviour that depends on the wording of an error message, no guard that blocks more than it must, and no two lists of the same phrases.

## Scope
**In (each item is one small, separately tested change; the number is the follow-up it closes):**
- **Notices (P2-16):** *N1* `GET /api/portfolio/notices` becomes read-only: the attention items are reconciled by the healer tick and at the end of every sync run, and the GET only reads (two tabs can no longer flicker an item). *N2* retention of `attention_items`: resolved rows older than 30 days are deleted by the healer tick (documented DELETE exception for that one table, ADR 0030). *N3* a new attention kind **`rows_dropped`** (ISS-09): when the latest trades or ledger run dropped rows for a reason other than a harmless zero amount, an info/warning item says how many and why (counts and reason codes only) with an action to the sync line; it resolves when the latest run of that kind drops nothing. *N4* the client's command-target list is generated from the server's list (one source of truth, like the hub contract) instead of a hand-copied fixture.
- **Failures (P2-16):** *F1* the Dhan clients carry a **structured failure kind** (transient, rate-limited, rejected, token…) so `SyncFailures` and the retry rules no longer match message text. *F2* callers of `PortfolioUnitOfWork.RunAsync` check `IsFailure` (row-by-row save, finish, price-alert check) and manual flows get a neutral message instead of "the next run tries again". *F3* `SyncGuard` becomes kind-aware: trade Undo/Rebuild/import wait for holdings and trades runs only, DP import for holdings and ledger runs only, demat for none; the message says it can take a few minutes. *F4* a run that fails late no longer re-reads the same noted windows on every retry (progress per window is kept).
- **Imports (P2-17):** *I1* one phrase table for `DepositoryCharges`, `LedgerClassifier` and `LedgerCosts` (no dead phrases, no list that disagrees); *I2* the supersede pool decision and `DpPlanner` use the same whole-day rule; *I3* `BillPlanner` identities no longer depend on which siblings are pending (covered by Dhan or skipped rows count as siblings); *I4* a decompression-bomb guard for `.xlsx` (total uncompressed size and entry count checked before parsing); *I5* `DividendUploads.Lines` caps each `includeDuplicates` value and stops at `MaxRows`.
- **Returns and screens (P2-14/15):** *R1* the Cash drawer says only account-level ledger costs count in the net return; *R2* the dividend suggestions list refreshes when a sync run ends with the Cash panel open; *R3* per-share amounts show their 4 stored decimals; *R4* a mixed priced/unpriced scope has a test and a stated rule (dividends and charges of unpriced holdings are in the percentage but not in the net XIRR); *R5* the toast's remembered focus target is cleared when no open toast holds focus; *R6* a database test for the "holding appeared since" check and its margin.

**Out (not in this slice):** anything that needs the owner's real files (the DP date assumption, the labelling of Dhan's ledger, ISS-01/02/06 — they wait for the uploads); `needs_reread` carrying the dominant reason (ISS-10, needs a migration on `broker_sync_windows`; stays open); positions and F&O; reconciliation against Dhan's realised-P&L statements.

## Acceptance criteria
1. **N1** `GET /api/portfolio/notices` issues no INSERT/UPDATE/DELETE (a statement-recording test proves it); items appear within one healer tick after the cause and immediately after a sync run ends; two parallel GETs never change the stored state; the items, ids and `since` values are unchanged for the existing six kinds.
2. **N2** the healer tick deletes resolved `attention_items` older than 30 days (setting `Portfolio:Heal:AttentionRetentionDays`, default 30); unresolved rows are never deleted; the runtime role gets DELETE on that one table only (grant test) and the other tables keep `NeverDelete`.
3. **N3** `rows_dropped` per account and kind: severity info when the only reasons are benign (`zero_amount`), warning otherwise; text with counts and plain reason words (no names, amounts, ids); action *Open* → `/portfolio?focus=sync`; id `attention:dropped:{account}:{kind}:{hash of reason set}`; resolves when the latest run of that kind has no drops; a run that drops nothing after one that did resolves it; tests for each rule and for the frontend text of every reason code.
4. **N4** the commands the server can emit are listed once (a static list in the Application layer); `scripts/gen-api.sh` writes `frontend/src/shared/api/attentionCommands.generated.ts`; `commands.ts` allows exactly that list; the hand-copied fixture is deleted and a test fails if the generated list and the deriver diverge.
5. **F1** a Dhan failure carries a `kind` (enum) set where the HTTP status/code is read; `SyncFailures` and `ConnectionErrors` use it; no code matches an error message text; reworded messages cannot change a retry decision (test: change every message text in the mapping and the decisions stay).
6. **F2** every caller of `RunAsync` handles a failed result; the manual-flow message is neutral; tests for the ignored-result paths.
7. **F3** the guard is kind-aware as stated; a ledger-only run does not block trade Undo/Rebuild; a trades run blocks them; messages say "can take a few minutes"; tests per operation × run kind.
8. **F4** the re-read stage marks each window done as it finishes; a late failure retries only the windows not done; test.
9. **I1–I5** one phrase table with tests that pin the matched phrases; supersede and `DpPlanner` agree on a whole-day fixture; identity tests with covered/skipped siblings; an `.xlsx` with inflated entries is refused with a clean 400 before parsing; `includeDuplicates` caps tested.
10. **R1–R6** as stated, each with a test; the frontend shows `rows_dropped` items (texts for all 14 reason codes reused from `dropReasons.ts`).
11. Nothing changes in what the owner sees for the six existing attention kinds, the sync line or the imports except the items above (regression tests stay green).

## Contract
- **API:** no new endpoints; `rows_dropped` is a new notice `kind`; the generated `attentionCommands.generated.ts` is new. **Data:** migration `AttentionRetentionGrant` (DELETE grant on `portfolio.attention_items`; a per-window done marker only if needed for F4 — prefer the existing `broker_sync_windows` rows). **Settings:** `Portfolio:Heal:AttentionRetentionDays`.

## Non-functional
No new Dhan calls. The healer tick stays within its minute; reconcile and retention are set-based (one pass, no per-account queries).

## Test plan
Statement-recording test for N1; grant tests for N2; deriver table tests for N3; contract-generation test for N4; unit tests for F1–F4, I1–I5; frontend tests for R1–R5 and `rows_dropped`; regression run of the whole suite and `npm run test:clock -- <two instants>`.

## Follow-ups from the final review (minor, not blocking)
- No test shows that a failing reconciler or retention in `PortfolioRefreshRunner.HousekeepAsync` does not stop the first refresh from starting (the catch is correct by reading).
- At start-up the reconcile and retention run twice (`HousekeepAsync` and the first healer tick): harmless, set-based and serialised.
- `frontend/src/test/nodeFs.d.ts` sits under `src`, so production code could import `node:fs` and still typecheck; the contract tests use paths relative to `frontend/` (`../backend/...`) and the drop-reason drift test scrapes C# source with a regex and a fixed count of 14 — move the declaration to a test-only tsconfig and resolve paths from the test file.
- `useSyncFocusRequest` observes `document.body` for the life of the page when `?focus=sync` stays in the URL and the sync bar never mounts (for example no portfolio account): add a cap (about 10 s) that disconnects.

## Done when
- [ ] All acceptance criteria have evidence
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors (follow-up pass, 2026-10-15)
- [x] Contract regenerated (`scripts/gen-api.sh`); migration added (`AttentionRetentionGrant`)
- [x] Design docs/ADR 0030 updated (doc 12, 13, 16; ADR 0027 and 0029 amended); roadmap ISS-08, ISS-09 closed

## Amendments (2026-10-15)
What the build did differently from the text above; [ADR 0030](../adr/0030-read-only-notices-retention-and-structured-failures.md) lists the same under "Deviations from the spec". The design is in [doc 12](../design/12-portfolio-monitoring.md) §3 and [doc 13](../design/13-reports-and-ui.md) "Notifications and pop-ups".
- **F4 keeps the re-read progress in the run's counts JSON, not in `broker_sync_windows`** (the *Contract* paragraph preferred the window rows). `BrokerSyncCounts.RereadDone` (dates only) and `RereadVersion` are written in the unit of work that stores each window read again; the next run takes the list over from the failed run before it when the mapper version is the same. No migration was needed for it; the only migration is `AttentionRetentionGrant`. ISS-10 (the reason on `needs_reread`) still needs a column on `broker_sync_windows` and stays open.
- **I1 leaves four qualifier phrases** (*depository*, *dp fee*, *demat fee*, *subscription fee*): listed once in `LedgerText.Phrases` without the charge flag, so they qualify only an entry that another phrase made a charge, as the three lists did before. "No dead phrases" holds because each phrase is listed once with its flags.
- **I3 changes one identity:** a BSE trade whose same-bill NSE sibling is left out now carries the exchange salt; a report imported before and again after the change can store such a BSE row a second time.
- **`PortfolioErrors.HoldingChanged` carries the cause `DataChanged`** (reason code `data_changed`, treated as transient), so a run that ended because a unit of work collided twice is retried quietly under its own plain-word reason; manual flows get the neutral `DataChanged` text.
- **Criterion 10 (the text of `rows_dropped`):** the item's text is built on the server (`DropReasonWords`, the 14 codes), and the frontend shows it as sent; `dropReasons.ts` is still the table of the sync line, so the same words exist in both tiers instead of one being reused.
- **Criterion 9 (the `.xlsx` guard):** a refused package is `portfolio.invalid` on the field `file`, before parsing (the error of that file), rather than a separate status of its own.
- **Extras:** `Portfolio:Heal:AttentionRetentionDays` accepts 1 to 3650; a reconcile also runs after a Dhan sign-in is stored (`CompleteConnect`), so the "reconnect" item resolves at once; only the summary of the tradebook import endpoint was reworded for the kind-aware guard.
