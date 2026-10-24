# P7-09 — Wire the pipeline end to end plus the suggestions service

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; the owner's binding decisions of 2026-10-23 apply: P7 places no order, tickets go to paper accounts only, default autonomy Advisory and any auto approval is a setting defaulting OFF, reserve promotion off, no automatic learning change, no depth recording). Built 2026-10-24. Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-09.
- **Design refs:** docs/design/10-trade-selection.md §2 to §6 · 04 §3, §4.3 · 15 §1, §2 · 19 §4, §5 · ADRs [0082](../adr/0082-selection-core-filters-with-unavailable-merge-and-a-deterministic-rank.md) to [0091](../adr/0091-auto-approval-is-an-option-behind-an-owner-setting-default-off-a-new-approval-kind-the-database-checks.md); ADR 0092 (this slice).

## Assumptions (stated up front; each reversible)
1. **Safety: nothing here touches orders, live accounts, credentials or approvals.** The builder only builds a plan; the store inserts it as Proposed; nothing approves, arms or places anything. Allocations go to **paper** Trading accounts only (the allocator, the checklist and the database check each refuse anything else). `IChecklistSource` stays the null source, so the auto-approval rule (P7-12, OFF by default) still declines every ticket.
2. **The switch stays OFF and the nightly stays paused.** `Planning:Builder:UseSelection` is false by default; with it off the registered builder is the `NullPlanBuilder` and its plan is byte for byte what it was (a test pins the key, basis and hash). Turning it on, and switching the `nightly` and `suggestions` services on, are the owner's acts.
3. **Order of the wiring:** P7-01 selection, P7-06 smart rules, P7-02 portfolio fill on the smart-rules survivors, then for each primary ticket P7-07 allocation and P7-05 checklist.
4. **No data source is passed as unavailable (null), never as a pass:** market figures (VIX, breadth, events: P3-01d/e are not built, so the gate is *unavailable* and the plan is flagged), real taken trades (the throttle is unavailable), cost, events, F&O ban, surveillance, data-quality status per instrument, results dates, price band, average daily volume (the checklist's liquidity check), correlation with the book (checklist), the funds of a broker-funds account.
5. **The open book is UNAVAILABLE by default (owner decision, 2026-10-24, from "unavailable never passes" and "behaviour that weakens safety is a setting, default OFF").** The system keeps no position record for a Trading account (no journal yet, P7-14), so the book, the open risk and the loss today are null, the portfolio fill takes nothing (`book:unavailable`), no ticket is primary, and the basis starts its open-book flag with "Open book unavailable: no primary tickets". A suggestion (`open_book_unavailable`, one per plan) says so. Only the owner setting `Planning:OpenBook:AssumedEmpty` (config only, no endpoint, only the exact text `true` counts; the setting `planning.open_book_assumed_empty`, default false) makes the book the empty list and open risk and loss zero; then the basis and every primary ticket carry "Open book: assumed empty by owner setting (positions held elsewhere are not counted)." The hand-checked fixture runs with the setting true; a second fixture proves the default gives zero primary tickets with the flag.
6. **Correlation** is the Pearson correlation of daily close-to-close returns (60 common sessions at most, 30 at least) from `bars_agg` 1D, unadjusted for corporate actions, truncated at the close of the session; a pair with too few sessions is unknown, and an unknown pair blocks the fill pick (unavailable never passes).
7. **Allocation inputs:** entry price is the close of the session (indicative; said on every primary ticket), the stop is the ticket's level, lot size 1 (cash equities), the ticket's pool is 1 % of the average daily turnover divided by the entry (the same 1 % the checklist uses for liquidity). A ticket without a stop level, entry or turnover is unavailable and allocated nothing. The smart-rules risk multiplier scales each account's risk per trade. Earlier tickets of the plan use up an account's risk headroom and funds before later ones are sized. Equity is the fixed capital of the account; broker-funds accounts are unavailable (no funds read here).
8. **Fill risk per ticket** = the largest paper account's risk per trade (with overrides) times the smart-rules multiplier; with no paper Trading account it is unknown and the fill takes nothing (the plan says "No Trading account to size for"). Fill limits are the design defaults (`FillConstraints`); the selection rules are the defaults (so a default-rules plan holds most tickets for unavailable cost, events, ban and surveillance, as P7-01 intends).
9. **Where the checklist is stored.** The P7-03 store has no checklist column. For each primary ticket the checklist is computed for every Trading account, and each check that is not a pass (and each account that got nothing) is stored in the evidence's list as `checklist:<code>:<fail|unavailable>` and `allocation:<code>`, deduplicated. The review (P7-10) recomputes the full checklist from live inputs. No migration for this.
10. **Plan flags** (the banner: gate unavailable, correlation figures missing, throttle unavailable, risk multiplier, the book note) are written into the plan's basis after the sentence "Proposed, provisional: no order exists, nothing here is approved."; the column holds 500 characters and the text is cut there with an ellipsis (the gate flag comes first).
11. **Suggestions** are rules only (no model before P9): the open book was unavailable; the market gate had no data; no primary ticket; tickets held because one input is unavailable (one suggestion per input, naming up to 8 tickets, not one per ticket, to keep the inbox short); the newest cell statistics older than 7 days before the plan date (or none). Stored in `workflow.suggestions`, append-only, one row per plan, kind and subject. The `suggestions` service follows `nightly` and starts paused. No endpoint yet (the review workflow, P7-10, reads them); no snooze or expiry columns yet (they belong to the inbox endpoints).
12. Names: log ids 4740 to 4749; ADR 0092; migration `AddSuggestions` (with Down); no API change, so no contract regeneration.

## Goal
When the owner switches the builder on, the nightly plan is a real proposal: ranked, gated, filled against the limits, sized per paper account and checked, with every missing input named; and a deterministic inbox of suggestions says what the owner should look at. Nothing is approved.

## Scope
**In:** `SelectionPlanBuilder` wiring, `PlanWiring`, port `IPlanContextSource` and `StoredPlanContextSource`, `ReturnSeries`, `CorrelationTable.Matrix`, `TicketCandidate.InstrumentId`, `SuggestionRules`, `ISuggestionStore` and its store, migration, `GenerateSuggestionsHandler`, `SuggestionsService`, wording, logs, ADR, docs, tests.  
**Out:** any endpoint or screen, the review workflow, snooze/expiry, approval of any kind, the market gate data, trades/journal, replicate/distribute modes, live accounts, depth, the SignalR `nightlyStatus` event (no consumer yet), notifications beyond the existing service failure.

## Acceptance criteria
1. The seeded fixture reaches the hand-checked plan: primary AAA, BBB; reserve CCC (sector and mix limits), DDD (same bet as a better ticket); rejected FFF (sector unavailable), EEE (win rate fail); allocations 2000 and 500 shares to the paper account, nothing to the live account; checklist standings with unavailable inputs listed. *`WiredPlanEndToEndTests`.*
2. A market gate without data flags the plan (basis and suggestion); a no-trade gate moves every candidate to the reserve; no correlation figures leave one pick; no paper account takes nothing. *same.*
3. Determinism (any input order, same hash; a changed input, another hash) and the context is asked only for the ranked symbols, as of the session. *same.*
4. Truncation: bars after the close of the session, added, changed or removed, change neither the daily-return series nor the stored context. *`ReturnSeriesTests`, `PlanWiringIntegrationTests`.*
5. A dry run of the nightly with this builder writes nothing. *`WiredPlanEndToEndTests`.*
6. With the switch off the plan is the skeleton's, byte for byte; the builder is the null builder by default. *`NullBuilderUnchangedTests`, `PlanBuilderChoiceTests`.*
7. The first-plan wording and the gate marker are pinned; the engine's gate text starts with the marker. *`SuggestionRulesTests`, `WiredPlanEndToEndTests`.*
8. Suggestion rules by table, order-independent; the handler stores them; the service follows `nightly` with no schedule issue and starts paused. *`SuggestionRulesTests`, `SuggestionsTests`.*
9. `workflow.suggestions` is insert-only for the runtime role and a second identical suggestion is skipped. *`PlanGrantsTests`, `PlanWiringIntegrationTests`.*
10. Nothing in the plan or its types approves, arms or orders; the no-order architecture rule and the grants rule stay green. *Architecture and grants tests.*

## Contract
- **API:** none. **Data:** migration `AddSuggestions` (`workflow.suggestions`, unique `(plan_id, kind, subject)`, insert-only grants; Down drops it). **UI:** none.
- **Settings:** `Planning:Builder:UseSelection` (false by default). Services `nightly` and `suggestions` (paused by default).

## Non-functional
Per run: one read of accounts, profiles, holdings, one sector and one basket lookup for the ranked instruments, and one daily-bar read per ranked instrument (about 120 calendar days). Logs carry counts and codes only (4740 to 4743), no account id, no price.

## Honest limits
The plan is a proposal on about 2.1 years of replayed history. The gate is unavailable until P3-01d/e. Sizes use the close as the entry. The open book is unavailable by default; when the owner sets it assumed empty, risk headroom is the account's whole limit and positions held in the broker's own app are not counted. Correlation is on unadjusted closes. Cells are not paper-proven (provisional). Allocation is the pool split of ADR 0089 (no replicate/distribute, no cross-account caps).

## Open questions
None blocking. For the owner later: whether to set `Planning:OpenBook:AssumedEmpty`; whether to turn the builder, `nightly` and `suggestions` on.

## Done when
- [x] Criteria have evidence
- [ ] `scripts/check.sh` green
- [x] ADR 0092, roadmap row, docs built
