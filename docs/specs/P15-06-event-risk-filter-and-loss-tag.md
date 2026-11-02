# P15-06 — Event-risk filter and loss tag: `not_event_day`, `exclude.results_within_days`, the plan check "results within N days" and the results loss tag, `unavailable` never a pass, behind a default-off switch with an auto option

- **Status:** Approved (owner, 2026-11-02: pre-approved in the autonomous loop; open questions resolved with the recommended/safer answers listed in the spec)
- **Phase / roadmap row:** P15 — slice P15-06 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Event-risk filter and loss tag").
- **Design refs:** docs/design/07 §1 (`filters`), 10 §2 (events excluded), 09 §14 (smart rules, loss tags), 11 §2, 15, 16 (`exclude.results_within_days`, `smart_rules`), 19 §1, §5 · ADR 0037 (engine version, run key), 0071 (loss tags), 0082 (selection filters, `unavailable`), 0087 (pre-trade checks, `unavailable` never passes), 0091/0128 (fresh password), 0083 (no order) · ISS-24 (the refusal) · CLAUDE.md "Approvals have an auto option". New ADR: **next free number at build time**, plus an ADR 0037 amendment (engine `sim-7`).
- **Needs:** P15-02, P15-04 (`IResultsCalendar`, the stored calendar). **Needed by:** P15-10 (protocol F evaluates this filter and may switch it on by KEEP), P15-11 (screens).
- **Size:** 3 agent-days (estimate; the plan said 2, +1 for the two checklists, the run-identity change and the switch with its auto option).
- **Safety:** **yes** — it changes which entries a research run takes and, when switched on, which candidates the plan selects. Default OFF; an owner act with a fresh password; the auto path is recorded as auto and can never run on `unavailable` input. **It never places, sizes, moves or cancels an order**; no setting here allows one.

## Goal
Four things read the results calendar and nothing else: (1) a strategy that names `not_event_day` runs in the simulator instead of being refused; (2) the plan selection can exclude a candidate whose results are within N sessions; (3) the plan's pre-trade check "results within N days" returns a real answer instead of `unavailable`; (4) a loss that was open across a results broadcast carries a `news_results` tag. For every instrument without a calendar entry each of them says `unavailable`, which is never a pass. In the live paths nothing changes until the owner turns the switch on.

## Verified facts (read 2026-11-02)
**Code:**
- **Model and simulator.** `FilterNames.All` is `not_event_day` and `min_turnover`. `ModelChecks` **accepts** both with the warning `FilterNotEvaluated` ("there is no results calendar yet (P15)"). `Simulator` (around line 314) **refuses every filter** a side names with `simulation.filter_not_available`. Engine version `SimulationEngine.Version = "sim-6"`; the run key and `research.sim_runs` carry `engine_version` and `definition_sha256`; ADR 0037 amendments map each engine label to the SHA-256 of its golden records.
- **Plan selection.** `EligibilityFilters` has the filter `FilterCodes.Event` reading `PlanCandidate.ExcludedByEvent` (`bool?`, null = unavailable); by default every filter is *required*, so a candidate with a null event answer is held (ADR 0082). A search of `backend/src` finds **no code that sets `ExcludedByEvent`**.
- **Pre-trade checks.** There are **two** result checks: `PreTradeChecklist.Results` (Domain/Checklist, plan, limit `ChecklistLimits.ResultsWithinDays` 3) and `ExecChecklist.Results` (`XDrishti.Execution`, limit `ExecLimits.ResultsWithinDays` 3). Both read `DaysToResults` (null = `unavailable`; a pass needs `d > limit`). **Nothing sets `DaysToResults`** in either path. The execution service runs under the role `xd_exec`, which this migration series does not grant on schema `filings` (P15-02).
- **Loss tags.** `LossTagger.RuleVersion = "lt-2"`; `LossTagKind` has seven kinds and none for results; `LossTagReport.NotBuilt` lists "news_results: needs an event calendar (P15)". ADR 0071: the tags are "a function of the trade alone" and must never become decision-time features.
- **Settings.** `smart_rules.*` keys are listed through `PaperConfigKeys.SmartRulesPrefix` and `ConfigSnapshotSource` (by reflection over the smart-rule options), so a new smart-rule field enters a paper account's configuration snapshot; the build confirms where the switch is surfaced.
**Plan corrections found while verifying (listed in the report):** the plan names one pre-trade check; there are two, and only the plan one is changed here. The plan puts "the evaluation of protocol F" in this slice; the evaluation belongs to P15-10. The model **accepts** `not_event_day` (with a warning); only the simulator refuses it. `min_turnover` is refused too and **stays refused**.

## Scope
**In:**
- `EventDayFilter` and `ResultsWindow` (pure, `Domain`), used by the simulator, the selection and the checklist.
- Simulator: `not_event_day` evaluated against an immutable `EventCalendarSnapshot` passed with the run request; engine `sim-7`; run-record schema +1; refusal list: `not_event_day` leaves it, `min_turnover` stays.
- Plan selection: `ExcludedByEvent` supplied from the calendar when the switch is ON.
- Plan pre-trade check: `DaysToResults` supplied from the calendar when the switch is ON (`PreTradeChecklist` only).
- Loss tag `NewsResults` (`lt-3`).
- The switch `smart_rules.event_filter.*`, its history table `filings.event_filter_decisions` (migration `AddEventFilterDecisions`), `GET`/`PUT /api/selection/event-filter`, and the pure auto-decision rule.
- Model warning text, docs (07, 10, 11, 16, ISS-24), ADRs, tests.

**Out:** evaluating the filter (protocols F and X, the verdict: P15-10, which also calls the auto-decision rule); `min_turnover`; the **execution** checklist (`ExecChecklist.Results` stays `unavailable`: `xd_exec` gets no grant on `filings`; how the execution service receives facts is a money-path decision outside this slice, recorded in the ADR); any estimate of a date; news or announcements as an event source (P15-07/08); a screen (P15-11); any order, size or exit change.

## The rules
**Window.** `days_to_results` is the P15-04 value in sessions (0 on the event session, `unavailable` when there is no visible entry).
- `exclude.results_within_days N` (plan selection): a candidate is **excluded** iff `0 ≤ days_to_results ≤ N`. Default `N = 2` (doc 16), range 0-10.
- `not_event_day` (strategy filter, no parameter): entry is **blocked** iff `0 ≤ days_to_results ≤ H` at the decision instant, where `H` is the side's `time_rules.max_hold_sessions` (0 for a side that closes within the day). The rule is "no position is open across a results event".
- Plan pre-trade check: unchanged rule and limit (3): pass iff `d > 3`, `fail` otherwise, `unavailable` when `DaysToResults` is null.
- Every answer is evaluated at the decision instant `t` with the P15-02/04 visibility rule: a meeting intimated after `t` does not exist at `t`.

**Hand examples** (results event session Wednesday 2026-08-12; Friday 2026-08-07 is a session):

| Decision date | `days_to_results` | `exclude N=2` | `not_event_day`, H = 3 | `not_event_day`, intraday (H = 0) | plan check (limit 3) |
|---|---|---|---|---|---|
| Fri 08-07 | 3 | taken | blocked | taken | fail (3 is not > 3) |
| Mon 08-10 | 2 | excluded | blocked | taken | fail |
| Tue 08-11 | 1 | excluded | blocked | taken | fail |
| Wed 08-12 | 0 | excluded | blocked | blocked | fail |
| Thu 08-06 | 4 | taken | taken | taken | pass |
| any, no entry | unavailable | unavailable | unavailable | unavailable | unavailable |

## Acceptance criteria
1. **`unavailable` is never a pass.** In every consumer an instrument with no visible calendar entry gets `unavailable` and the consumer's non-pass path: the simulator takes **no entry** and counts it (`event_filter_unavailable`); the selection filter outcome is `Unavailable` (the candidate is held unless the owner listed `event` in `OptionalFilters`, in which case it stays a printed caveat); the plan check is `unavailable`; the loss tag is not assigned and counted `news_results_unavailable`. A test per consumer, plus a roll-up test that no code path maps unavailable to pass.
2. **Simulator, `not_event_day`.** Given a strategy whose side names `not_event_day` and a run request carrying an `EventCalendarSnapshot`, then the run executes (it is no longer `filter_not_available`), blocks entries by the rule above using only rows visible at each decision instant, and records per run: `event_filter_blocked`, `event_filter_unavailable`, `calendar_rows` and `calendar_sha256` (hash of the canonical snapshot) and `evf_version`. Hand cases: the table above on a fixture series with one results date. With the filter named and **no calendar row at all in the window** the run is refused `simulation.filter_not_available` with the message "no results calendar rows for the window (P15-04)", before any bar is read. A strategy that does not name the filter reads no calendar and its trades equal the `sim-6` trades (a differential test on the existing fixtures).
3. **Refusal list.** `not_event_day` leaves the unconditional refusals; `min_turnover` is refused exactly as before with the same code and a message that no longer mentions the results calendar; the model warning `FilterNotEvaluated` for `not_event_day` is removed (the filter is evaluated) and kept for `min_turnover`. The tests that pin these (`ModelChecks`, `Simulator`, `WarmupStrategies` fixtures) are updated deliberately. ISS-24 in the roadmap and the texts in docs 07 and 11 are updated.
4. **Engine version and run identity (ADR 0037).** `SimulationEngine.Version` becomes `sim-7`; the run-record schema number increases by one for the new counters; the golden files are regenerated **on purpose** with the engine label and counter diff only (the diff is reviewed: nothing else changes for strategies without the filter); `RunAssemblyTests` maps `sim-7` to its golden hash. A run that names the filter has a run key that differs from the same strategy without it (the definition changes) **and** from the same strategy over a different calendar snapshot (the snapshot hash and the lag are part of the key). Old `sim-6` runs stay readable with their label and are not re-labelled.
5. **Selection (`exclude.results_within_days`).** With the switch OFF, `ExcludedByEvent` stays null exactly as today (no behaviour change, tested). With the switch ON, it is `true` when `0 ≤ days ≤ N`, `false` when `days > N`, null when unavailable; the existing `event` filter and the ranker's event-risk input are unchanged. The N used and the calendar coverage figure appear in the selection output (`events: N=2, 187 of 214 instruments with an entry, 27 unavailable`).
6. **Plan pre-trade check.** With the switch OFF, `DaysToResults` is null (as today). With it ON, it is the P15-04 value (a negative or unavailable value stays null); `PreTradeChecklist.Results` and its limit are unchanged. `ExecChecklist.Results` is not changed and a test pins that `XDrishti.Execution` and the `xd_exec` role have no reference to or grant on `filings`.
7. **Loss tag.** `LossTagKind.NewsResults` (appended last in the enum so stored counts keep their order; assigned before `NormalVariance` so a results loss is not called ordinary variance), rule version `lt-3`. A tagged loss is a trade with R below 0 for which an *event instant* of its instrument lies in `[entry, exit]` (both inclusive). The event instant is the `broadcast_ts` of a stored `results` filing (state `ok`) for that instrument; when no results filing exists for a calendar meeting whose event session lies in the trade's span, the instant is that session's 09:15 India time and the tag detail says `basis: calendar`. `LossTagger.Tag` gets an overload taking the instrument's event instants; the existing signature is kept and means "events not supplied" (no tag, counted `news_results_unavailable`, never "no news"). Detail text: `results broadcast 2026-08-12 16:12 IST while the trade was open (entry …, exit …)`. The line "news_results" leaves `LossTagReport.NotBuilt` only when events are supplied; otherwise it stays with the reason "no filings data for N instruments". The tag is descriptive of the past, uses data known after the trade, and is **never** a decision-time feature (the ADR 0071 architecture test is extended to this type).
8. **The switch.** `smart_rules.event_filter.enabled` (default OFF), `smart_rules.event_filter.results_within_days` (2) and `smart_rules.event_filter.approval` (`manual` default, or `auto`) are changed only by `PUT /api/selection/event-filter {enabled, resultsWithinDays, approval, reason, password}`:
   - a fresh password on every change, ON and OFF (`StepUpCommand`, `Elevate: false`); the actor from the session (`owner:...`); account event `EventFilterSwitched`, category `selection`; 401/423 and 400 `selection.reason_invalid`, 400 `selection.invalid_setting` for `resultsWithinDays` outside 0-10;
   - every change appends a row to `filings.event_filter_decisions` (insert-only for `xd_app`; `id`, `enabled`, `results_within_days`, `approval`, `decided_by in (owner, auto)`, `actor`, `reason`, `setting_snapshot jsonb`, `verdict_ref null`, `changed_at`); checks: `decided_by = 'owner'` ⇔ `actor LIKE 'owner:%'`; `decided_by = 'auto'` ⇔ `actor = 'auto:event-filter'` and `verdict_ref IS NOT NULL`; the effective value is the newest row, no row = OFF, an unreadable table = OFF;
   - `GET` returns the effective switch, the settings, the history (latest 50) and the reasons the filter is not acting (switch off, no calendar rows, no entry for N instruments).
   The smart-rule snapshot of a paper account lists `smart_rules.event_filter.enabled` so the configuration identity changes when it does (tested).
9. **Auto option (CLAUDE.md).** With `approval = manual` (default) only an owner PUT can turn the filter on. With `approval = auto` the pure rule `EventFilterAutoDecision.Decide(verdict, current)` may turn it on **without** an owner approval, and the decision is recorded as `decided_by auto`, `actor auto:event-filter`, with the settings snapshot and the verdict reference, never as an owner approval. Rules, all required:
   - the newest stored protocol-F verdict (`IEventFilterVerdict`, implemented by P15-10) for the **current** rule version is `KEEP` with its evidence block; any other state (`DROP`, `INCONCLUSIVE`, `NOT_EVALUABLE`) or `unavailable` or no verdict = no change, counted `auto_not_applied` with the reason;
   - an auto-enabled filter turns itself OFF (recorded auto) when the newest verdict stops being `KEEP`; a filter turned on by the owner is never turned off by the auto rule;
   - the auto path never changes `resultsWithinDays` or `approval`; setting `approval = auto` is itself an owner act with a fresh password.
   Until P15-10 exists the port has no implementation and answers `unavailable`, so the auto path cannot fire (a test pins this). Tests use a fake verdict port.
10. **No order.** The no-order tripwire and the Execution architecture tests are green; nothing in this slice calls an execution port; with the switch ON a plan item is excluded or held, never sized, placed, edited or cancelled. A failing calendar read makes the consumers answer `unavailable`, never throw into the plan or the run.
11. **Logging.** `selection.event_filter.switched|auto_applied|auto_not_applied|excluded|unavailable` carry counts, ids and codes only; no password, no file content.

## Contract
- **API:** `GET`/`PUT /api/selection/event-filter` (`ApiJsonContext`; `scripts/gen-api.sh`); problem codes `selection.reason_invalid`, `selection.invalid_setting`, `auth.invalid_credentials`, `auth.locked`, `selection.owner_only`.
- **Data (`AddEventFilterDecisions`, insert-only):** `filings.event_filter_decisions` as in criterion 8, identity id with the sequence grant, per-table grants (`xd_app` SELECT and INSERT; nothing for `xd_exec` or the read roles; grants test); `Down` drops it.
- **Settings:** the three `smart_rules.event_filter.*` values above; `Filings:LagMinutes` (P15-02) is part of the run identity.
- **Engine:** `sim-7`, run-record schema +1, `EventCalendarSnapshot` on the run request (immutable, built before the walk by one read; the walk does no I/O).
- **UI:** none in this slice (P15-11; the owner uses the API or a later small switch card as for P14).

## Non-functional
The snapshot read for a 5-year, 214-instrument window ≤ 1 s; the per-decision check is a dictionary lookup (the walk's speed is unchanged for strategies without the filter; a benchmark pins ≤ 2 % change). Selection and checklist add one batched `DaysToResults` call per plan run (≤ 100 ms for 214 instruments).

## Test plan
- **Unit (Domain):** the window table above for each consumer, boundary days, holiday and Saturday event sessions (via P15-04), `unavailable` for every consumer, the loss-tag rule (inside, equal to entry, equal to exit, outside, calendar basis, not supplied), the auto-decision table (KEEP, DROP, INCONCLUSIVE, NOT_EVALUABLE, unavailable, stale rule version, auto-off, owner-enabled untouched).
- **Simulation:** differential test (strategies without the filter equal `sim-6`), run key changes with the snapshot and lag, refusal with an empty snapshot, counters, regenerated goldens with the label diff only, `RunAssemblyTests`.
- **Application with fakes:** selection output with the switch OFF/ON, the plan check, the switch use case (fresh password, reason, audit event, auto record).
- **Integration (Timescale):** migration up/down, grants per role, check constraints, the endpoints (each state and error), the effective value from the newest row, snapshot listing of the setting.
- **Architecture:** the Execution project and `xd_exec` do not reference `filings`; the loss-tag type is not read by feature or decision code; no-order tripwire.

## Decisions (researched)
1. **`not_event_day` means "no position open across a results event".** It takes no parameter, so the hold length defines the window; a fixed N would be a hidden constant in a strategy file. Rejected: event day only (misses the swing overnight gap, the case P15-00 protocol F is about).
2. **Default OFF in the live paths; the simulator filter is explicit in the strategy.** A strategy that names the filter has asked for it; the plan selection and the checklist change only after an owner act (P15-00 open question 6).
3. **Switch state is an insert-only history, newest row wins.** Same pattern as the recorder switches (ADR 0135/0173); it gives the audit the auto option needs.
4. **Auto means the system applies a KEEP verdict; it never decides alone.** It reads a stored, evidence-bearing verdict, records itself as auto, turns itself off when evidence stops, and cannot run on `unavailable` (CLAUDE.md; ADR 0087).
5. **The execution checklist is left `unavailable`.** Giving `xd_exec` a read of `filings` widens the money-path role for a soft check; a plan-time check already warns the owner before the ticket exists.
6. **The loss tag uses the actual results broadcast first, the meeting date second.** The broadcast is what hit the trade; the date is the fallback and says so.
7. **`min_turnover` stays refused.** It needs liquidity data of the universe layer (doc 10 §2), not a calendar; touching it would widen this slice.

## Risks and honest limits
- Protocol F may return DROP: then the code stays (a filter the owner may still choose) and the verdict is shown; nothing turns it on.
- Removing results-window trades removes the largest winners too (P15-00 risk 7); the 50 % review flag and the tail test live in P15-10.
- Coverage is the owner's file coverage: an instrument without an entry is held back by a required selection filter (the safe default), which can empty a plan; the output prints the counts so the cause is visible.
- A results meeting date is a lower bound for the broadcast (results may come later the same day or next); the window margin and the loss tag's broadcast basis absorb it, not exactly.

## Open questions (owner), with the resolution used
1. Default for the live switch? **Resolved: OFF; turning it ON, and setting `approval = auto`, are owner acts with a fresh password.**
2. Should the auto option exist now? **Resolved: yes (CLAUDE.md), inert until P15-10 supplies a verdict.**
3. Window for `not_event_day`? **Resolved: the strategy's `max_hold_sessions`.**
4. Execution checklist? **Resolved: unchanged, `unavailable`.**
5. Default `N`? **Resolved: 2 (doc 16), the pre-trade limit stays 3.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer (Opus): no blockers/majors (told to look for any path where `unavailable` becomes a pass or an entry, any change for strategies that do not name the filter, any auto decision recorded as an owner decision or taken without a KEEP verdict, any reference to `filings` from Execution, and any order call)
- [ ] Migration `AddEventFilterDecisions` with `Down`; contract regenerated (`scripts/gen-api.sh`); goldens regenerated on purpose
- [ ] ADR written and the ADR 0037 amendment (engine `sim-7`); docs 07, 10, 11, 16 and ISS-24 updated; the no-engine-label-change claim for filter-free strategies evidenced by the differential test
