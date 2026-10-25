# P12-00 — Execution service: the plan, the order of build, the rollout ladder and the seam

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P12 — Execution service (6w, after P11). Row: "`xd-exec`: state machine, pre-trade checks, broker-side stops, kill switch, reconciliation; Dhan sandbox → assisted → semi-auto → auto; ISP static IP". Exit: "30 semi-auto sessions with zero reconciliation breaks".
- **Design refs:** docs/design/20-extended-modules.md §A · 03 §3 (Trading module, `IOrderExecutor`), §4, §5, §8 · 04 §1 (account modes), §4.1 ("enabling execution: only you") · 05 §7, §8 · 08 §1, §2, §5 to §7 · 10 §5, §6b · 16 (`execution`, `autonomy.always_ask`, `step_up_for`) · 17 §2, §3, §4 · 19 §4, §5 · ADR 0083, 0087, 0091, 0097 · P7-00, P10-00, P11-00 (this plan follows their form).
- **Slices:** P12-01 to P12-06 (this group) and P12-07 to P12-12 (modes, semi-auto, auto-live, screens, daily login and static IP, the phase check; written by another agent, referred to here by id and goal only).
- **Size of this group:** 16.5 agent-days (3 + 3 + 2.5 + 3 + 2 + 3), an estimate, not calibrated (P4 to P7 all overran). With P12-07 to P12-12 (3.5 + 4 + 3 + 3 + 2.5 + 2 = 18, their own Size lines) the group is about 34.5 agent-days; the roadmap's 6 weeks is the build, the exit criterion is wall-clock (see "Time").
- **Safety:** yes for every slice from 01 on. This phase puts the first order-capable code into the repository.

## Goal
Let the system send the owner's already-approved intraday trades to Dhan in a way that is **safe by construction and honest about what is unverified**: every order has a broker-side stop; no order is sent twice or lost without a trace; a duplicate, a runaway, a stale token, a wrong price or a lost connection stops the system instead of the owner's money; the broker's books are compared with ours all day and any disagreement freezes the account. Everything is OFF until the owner moves one account up a ladder, one step at a time, on stored evidence.

## What P12 may and may not do
- **May:** build `XDrishti.Execution` (the only project that names an order action), the container `xd-exec`, the role `xd_exec`, the schema `execution`; send place, modify and cancel requests to Dhan's order and super-order endpoints, only for an account whose effective mode is `semi_auto` or `auto_live`, in a process whose environment is `sandbox` or `live`; read the order book, trade book, positions, funds, profile and static-IP status; operate Dhan's kill switch and P&L exit when the owner enables them.
- **May not:** open a short (entries are BUY only); automatically correct a position (no corrective buy or sell for an `oversold` or mismatched position: freeze and the owner); place a market order, an after-market order, a bracket, cover or MTF order, a sliced order, an order for an instrument other than NSE cash equity intraday; carry a position overnight; increase a position other than by an owner-approved entry; resend a lost entry; place anything while a halt is active; change the static IP; weaken the P7 allow-list pin (`BrokerAllowListTests`) or the no-order tripwire (its test file has zero diff); put any broker order path outside `ExecutionEndpoints.cs`.
- **How the no-order rule is relaxed, in one place:** P12-01 section "How P12 relaxes the no-order rule". In short: `NoOrderPlacementArchitectureTests` is unchanged; `XDrishti.Execution` is exempt by being outside its seven-assembly list; `ExecutionBoundaryArchitectureTests` is the one new file that says where order verbs may live (namespace `XDrishti.Execution.Dhan` and one interface), who may reference the project (nobody), where the paths may appear (one file), and that it has no HTTP surface.

## What exists and what does not (verified 2026-10-24 unless marked)
- **Facts corrected 2026-10-25:** `deploy/compose.yaml` has only the profiles `tools` and `ops` (P12 adds `live` with `xd-exec` as its first member); existing log ids end at 4801; P12 uses 6600 to 6799 (this group 6600 to 6699).
- **Exists:** the plan record and its state trails (`trading.plans`, `plan_items` with levels as text, `plan_item_allocations` paper-only by a database check, `plan_events`, `workflow.approvals`; ADR 0083); the ticket lifecycle (10 states, 13 moves, `Armed` and `Triggered` are alerts); the pre-trade checklist (14 checks, `PreTradeChecklist`, refuses non-paper accounts; ADR 0087); auto-approval behind a setting (ADR 0091); `TradingMode` with `Paper` and `ManualLive` only; the vault (`IBrokerSecretVault`, tokens per account, expiry in `accounts.broker_connections`); the Dhan data clients and the allow-list `BrokerEndpoints` (eight read entries, pinned by the P7 phase check, ADR 0097); the live feed and `market.live_quotes`; the services framework; the Dockerfile targets api, worker, feed; the compose profiles `tools` and `ops` (core services have none); the role `xd_app` with default DML on new tables; the order-free tripwire.
- **Does not exist:** any order code, `XDrishti.Execution`, `xd-exec`, `xd_exec`, schema `execution`; numeric plan levels (P7-19; ticket levels are text today); funds, positions, order-book or order-update reads; a mode above `manual_live`; a static IP; sandbox credentials; a Dhan token that is order-capable on a whitelisted IP; a halt record; the market-wide inputs that Soft checks need (results dates, F&O ban, correlation, surveillance: P7-06, P15).
- **UNVERIFIED (each settled by a named step):** that Dhan rejects or ignores a repeated `correlationId` (P12-02 never relies on it); the leg types and part-fill handling of super orders (canary C1); whether Dhan squares off INTRADAY positions itself and when (C1); whether Dhan requires market protection for market orders (we never send them); the sandbox base URL, fills and super-order support (P12-02 smoke); the kill switch's reset and reverse call (P12-05 drill, C3); the sign convention of the P&L exit (P12-05); the freeze quantity of cash equities (no primary source; own caps apply); the exact rate semantics of the exchange's 10 orders per second threshold (secondary sources only); the content of the NSE FAQ (fetch timed out).

## The seam (decisions the slices share)
**D1. One project, one exemption, one control plane.** `XDrishti.Execution` holds Domain (pure), Application, Persistence and the Dhan adapter; nothing references it; `xd-api` instructs it only by inserting rows in `execution.commands`, and reads its rows; `xd-exec` has no inbound port. Reason: one place to audit, no second route surface. Rejected: an HTTP service, an Order type in the shared Domain.
**D2. The database is the log, the broker is the truth.** Every fact is an insert-only row; the state is the fold; the broker's order book and trade book decide what happened; any disagreement freezes (D8). Reason: the owner's rule "mismatch ⇒ freeze, never auto-fix" and ADR 0083's pattern.
**D3. Exactly once is built from three habits.** Write the intent to send (client order id) before sending; classify every send as acknowledged, not sent or unknown; for unknown, look the id up at the broker before anything else; never resend an entry; resend a protective leg only after two empty lookups. Reason: Dhan documents no de-duplication; a lost entry costs a trade, a duplicate costs a position. (P12-02)
**D4. `unavailable` is a reject, and some checks cannot be overridden.** 34 checks, 26 hard (no override by anyone), 8 soft (the owner may confirm in `semi_auto`, never in `auto_live`). With the P7 sources missing, only `semi_auto` with confirmation can clear a ticket. (P12-03; ADR 0087, 0091)
**D5. Broker-side stop first, by super order.** The stop and target legs live at Dhan from the entry; the protection invariant is judged from the broker's book within 10 s; a breach is repaired once and then flattened; stops only tighten; exits only reduce. Intraday, `FIXED_RR` and a tighten-only stop in this phase; partials, pyramids, EMA exits and swing stay out. (P12-04)
**D6. Easy to stop, hard to start.** A halt starts with a click; releasing needs the password and the conditions; exit-all needs the owner's explicit act or the pre-authorised setting (default off); Dhan's switch (usable only when flat) and P&L exit are opt-in belts. (P12-05)
**D7. Poll is the truth, the stream is the speed; recover before acting.** (P12-06)
**D8. Freeze, never fix; the owner resolves by an appended row.** (P12-06)
**D9. The mode ladder is the owner's, one step at a time, on stored evidence, with automatic demotion.** `paper → manual_live → assisted → semi_auto → auto_live`; only `semi_auto` and `auto_live` make the engine call the broker; `assisted` only prepares text. (P12-07; doc 04 §4.1)
**D10. Every automatic form sits behind a setting that defaults OFF** (the owner's rule of 2026-10-23): the tap's automatic form is `auto_live`, itself behind `auto_live_enabled`; auto-approved tickets reach `auto_live` only if a second setting allows (P12-09); every automatic action is recorded as automatic with the settings snapshot; no setting turns a hard check off. No setting in P12 lets an order be placed without a mode row, a process environment and an open gate.
**D11. P12 does not write the P7 plan tables.** The ticket's `Triggered` and `Closed` states, which today have no writer, are fed by a small bridge outside this group (Findings). The intent carries `plan_item_id` and `approval_id`; the plan stays a record of what was proposed and decided.
**D12. Entries are BUY only, and the first live days are a one-share canary, both enforced by checks, not by habit.** `product_segment` (P12-03) refuses any SELL entry until a future P12-13; the setting `exec.canary_pending` (P12-01, default ON) makes the Hard check `canary_cap` (P12-03) reject any entry above 1 share until the owner, with a password, records canaries C1 and C2 and turns it off. P12-08 and P12-09 reference it. An `oversold` position (a short) is a fault: CRITICAL alarm and `freeze`, no automatic fix (P12-04, P12-06). (rollout below)

## Slices and order
| Slice | Goal | Size | Needs | Gate to build | Safety |
|---|---|---|---|---|---|
| P12-01 | Project, container, role, schema, the 14-state machine (23 moves), event log, projection, client order id, gate, boundary test | 3 | P7-03 | none (no broker call) | yes |
| P12-02 | Dhan order adapter, the one endpoint guard, send protocol, `FakeBroker`, sandbox smoke | 3 | 01 | none; sandbox credentials are the owner's | yes |
| P12-03 | 34 pre-trade checks, `unavailable` = reject | 2.5 | 01, 02 | none | yes |
| P12-04 | Super-order stops, protection invariant, tighten-only, flatten, square-off, alarms | 3 | 01, 02, 03 | none | yes |
| P12-05 | Halts (4 levels, 17 triggers), kill switch, P&L exit, drill | 2 | 01, 02, 04 | none | yes |
| P12-06 | Order-update stream, poll, recovery, reconciliation, freeze, resolution | 3 | 01, 02, 04, 05 | none | yes |
| P12-07 | Mode ladder, evidence gates, demotion, assisted sheet, settings endpoints; extends `ck_plan_item_allocations_shape` to the remaining live modes (H2) | 3.5 | 01..06, P11-06 | P11-06 MET for live | yes |
| P12-11 | Daily login, token and static-IP operations, readiness; reads the SEBI/static-IP record of P11-05 (`trading.compliance_records`), no second table (H2) | 2.5 | 07, 02, P11-05 | owner's ISP IP | yes |
| P12-08 | One-tap confirm (H2) | 4 | 07, 11, 03, 05, P7-19 | 07, 11 | yes |
| P12-09 | `auto_live` caps and guard (H2) | 3 | 07, 08, 11, P7-19 | P12-12 MET | yes |
| P12-10 | Execution screens and notifications (H2) | 3 | 01..09, 11 | none | yes |
| P12-12 | The phase check: 30 semi-auto sessions, zero breaks (H2) | 2 + wall-clock | all | wall-clock | yes |
**Build order of the whole phase: P12-01 to P12-06, then P12-07, P12-11, P12-08, P12-09, P12-10, P12-12.** Order inside 01 to 06: 01, then 02; 03 and 04 can run in parallel worktrees after 02 (they share only ports and the settings registry; never two writers on `ExecSettingRegistry`: 03 first, 04 rebases); 05 after 04; 06 after 05 (it needs `freeze`); 07 may start after 01 in parallel with 02 to 06 (it needs only the schema and ports); 11 needs 07; 08 and 09 need 11. 01 to 06 build and test on `FakeBroker` and need nothing from the owner. **No live order exists before P12-07 to P12-11 and the owner's act.**

## Rollout ladder and the canary protocol (the rollout, not code)
1. **CI** on `FakeBroker` (every slice). 2. **Sandbox smoke** (explicit, owner credentials): wire contract only; third-party sources say fills are at a fixed price and sockets are absent (P12-02). 3. **Assisted** (P12-07): the system prints the exact order, the owner types it into Dhan; the system compares what was entered (10 sessions). 4. **Canary C1 (semi_auto, live)**: one ticket, **1 share**, a BUY of a liquid stock, `super` structure, the owner at the screen with Dhan's own app open. Record: leg order types of the stop and target, what the broker does on a part fill (not testable at 1 share: then repeat C1b with 2 shares at a thin moment only if the owner wishes), whether the stop leg exists from the entry, what time Dhan squares off, the exact stream fields, trade-book lag, positions fields, what `correlationId` looks like in the book, `algoId`. 5. **C2**: same with `plain_stop`. 6. **C3**: the kill-switch drill and a halt drill in the live environment (no order). 7. **Normal semi-auto** at the P11 ramp (25 to 50% of intended risk, P11-04), 30 counted sessions (P12-12). 8. **`auto_live`** only after P12-12 MET, behind its own switch, with hard caps (P12-09). A canary result that differs from this plan is written into an ADR amendment and the affected spec before the next step; any unexplained observation stops the ladder.

## Time
Wall-clock dominates: P11 is at least 8 weeks; the assisted gate needs 10 sessions; the exit needs 30 semi-auto sessions (about 6 weeks); the static IP can take days to buy and Dhan cannot change it again for 7 days after setting (Dhan page, read 2026-10-24). Realistically 2 months of sessions after the last slice is built, if nothing breaks (a break restarts the clean-session count of P12-12).

## Cut line (what goes first if the schedule or the evidence bites)
Never cut: 01 to 06, the canary, the halt, the freeze. Cut in this order: (1) `auto_live` (P12-09) and everything automatic beyond the tap; the exit criterion is about semi-auto anyway; (2) the broker P&L exit and broker kill switch belts (defaults off already); (3) the break-even move and trailing; (4) `plain_stop` fallback if the canary shows super orders behave. Shorts are not a cut: they are out of P12 (P12-13). Not cuttable without a new owner decision: the broker-side stop, the write-ahead, reconciliation freeze.

## Risks (and the control that answers each)
| Risk | Control |
|---|---|
| Runaway orders (a loop, a bug) | 2/s, 30/min, 200/day caps below the broker's 10/s, 250/min, 7,000/day; `runaway_rate` halt at 6 sends in 10 s; one intent per ticket and account; max order value and quantity; a halt survives restarts (P12-02, 05) |
| Duplicate order | write-ahead client order id, three-way outcome, lookup before resend, entry never resent, unique keys in the database (P12-01, 02) |
| Stale or wrong token, wrong IP | token margin 20 min check, static-IP check, `DH-901` and invalid-IP `DH-905` map to halts, readiness at 08:45 (P12-03, 05, 11) |
| Partial fills | stop must cover filled quantity within 10 s; cancel of a part-filled entry ends as `filled`; leg rank rule (P12-01, 04) |
| Circuit or price band, locked prices | quote circuit limits, 2-tick margin, 2% collar to LTP, stop-limit with escalation when the limit is gapped through (P12-03, 04) |
| Position without a stop | invariant, one repair, flatten by default, alarm, demotion (P12-04, 07) |
| Our picture wrong | stream plus poll, recovery before acting, reconciliation, freeze (P12-06) |
| The Mac or container dies | broker-side stops; optional broker P&L exit; recovery on start; halts in the database (P12-04, 05, 06) |
| Owner's manual trades in the same account | foreign activity explained, conflict halts entries, recommendation of a separate execution account (P12-06) |
| Over-trust in the fake | sandbox smoke and canary; the fake is never evidence of Dhan's behaviour (below) |
| Regulatory change | P11-05 runbook, the P12-11 recheck, sources below |

## Test strategy
- **Layers.** Pure Domain: lifecycle table with all 196 pairs, checks with boundaries, protection monitor, reconciler, halt policy, id scheme; each has properties (5,000 seeds) and a negative control that must be caught on at least 10 seeds. Integration (Testcontainers + `FakeBroker`): event store constraints, grants for `xd_app` and `xd_exec`, write-ahead, recovery at every crash point, the endpoints. Architecture: `ExecutionBoundaryArchitectureTests`; the old tripwire and `BrokerAllowListTests` stay green.
- **`FakeBroker` (built in P12-02, extended in 04 to 06).** A stateful in-process implementation of `IBrokerOrders` and the stream: Dhan's status set and the documented rules (super-order legs, 25 modifications per order, 10/s rate limit `DH-904`, `DH-901`, invalid IP `DH-905`, kill switch only when flat), a virtual clock, scripted price paths for matching limit and stop-limit orders, partial fills, and fault injection: drop the response after acceptance, delay or reorder or duplicate updates, disconnect the socket, 5xx, silent deletion of a stop leg, a manual (foreign) order, position drift, trade-book lag, end-of-day square-off, a double-sell race. Seeds print on failure. A global chaos runner over 1,000 seeds asserts the invariants: no client order id accepted twice; position never negative; every position protected or alarmed within the deadline; every illegal move refused; projection equals replay of the log; no automatic fix. Contract tests run on the fake in CI and, explicitly, on the sandbox.
- **Honest limit of the fake.** It encodes our reading of Dhan's pages. A passing suite shows the code does what we think Dhan does, not what Dhan does; the sandbox cannot show fills, and the canary is the first real evidence.
- **Not tested here:** latency to the exchange, behaviour in a fast market, exchange rejections beyond what the broker documents.

## Compliance and facts used (sources read 2026-10-24; primary documents not obtained are marked)
- Dhan limits: order APIs 10/s, 250/min, 1,000/h, 7,000/day; 25 modifications per order (https://dhanhq.co/docs/v2/). Static IP: primary and secondary, set once, modify at most once in 7 days, required for place, modify, cancel (https://dhanhq.co/docs/v2/authentication/); `DH-905` when the request IP differs, a token made before adding the IP must be regenerated (Dhan FAQ). Tokens: web token 24 h, API key and secret 12 months, one active token (same page).
- SEBI retail algo framework (circular of 4 Feb 2025, NSE circular NSE/INVG/67858 of 5 May 2025, in force from 1 April 2026) per broker notices and trade press: own strategies at or below 10 orders per second per exchange are tagged with a generic algo id and need no registration; above that, registration; static IP for API orders; daily login; broker is principal (https://fyers.in/notice-board/new-sebi-framework-for-retail-algo-trading-from-april-01-2026, https://zerodha.com/z-connect/general/a-comprehensive-overview-of-nses-circular-on-the-new-retail-algo-trading-framework, Dhan: "Algo Registration is required only if your strategy places more than 10 orders per second", https://dhan.co/support/platforms/dhanhq-api/could-you-provide-me-my-algo-id/). **UNVERIFIED against the primary SEBI and NSE texts** (the NSE FAQ PDF could not be fetched). The system stays at 2 per second. P11-05 and P12-11 re-check before the first live day.
- Personal use only (doc 19 §5): one owner, own accounts; nothing here is advice or a service to others.

## Honest limits
- Everything about Dhan's order behaviour is read from documents with known typos; nothing was observed. The first live day is the first observation.
- Sandbox fidelity is poor and partly third-party-described; the fake is ours.
- The Dhan token is not scoped to orders: any container with the vault key holds an order-capable credential; the barriers are code, role, network and the gate.
- 30 sessions of zero breaks say the machinery was consistent with the broker's data, not that the strategies make money (that is P10 and P11).
- Small size: the limits (₹1 lakh per order, ₹1,000 loss per trade) are assumptions chosen for a first live stage, not derived from the owner's capital.
- A stop-limit can be gapped through; a loss can exceed 1R; a flatten pays spread.

## Owner acts (nothing else is the owner's)
Approve each spec; create sandbox credentials (Keychain items named in P12-02); buy and register the static IP with Dhan (P11-05, P12-11); start the container (`xd-up.sh --exec`); move an account up the ladder (P12-07, password, cooling-off); be present for the canary; turn on `auto_live_enabled`, `exit_all_on_loss_halt`, `exec.broker_pnl_exit`, `exec.broker_kill_switch_on_halt` if ever wanted. Each defaults OFF and, until the owner acts, nothing in P12 sends an order.

## Open questions
None blocking the specs. (Owner acts above have the default "OFF / not done".)

## Findings for the index
1. **Ticket-state bridge (missing slice, about 1.5 days).** `Triggered` and `Closed` of the plan ticket have no writer once orders exist; P12 does not write `trading.*` (D11). A small service in `xd-api` should read `execution.intent_state` and move the ticket with the existing store as `system:`; place it with or after P12-10.
2. **Numeric levels dependency.** P12-03/04/08/09 need P7-19 (numeric entry, stop and target); without it there is no intent. Gate P12-08/09 on it.
3. **Allocation check.** `ck_plan_item_allocations_shape` accepts only `mode = 'paper'` today (verified); P11-04A extends it to `manual_live` and P12-07 extends it to the remaining live modes, on the owner's decision.
4. **Schema and id alignment with H2.** H2's specs name `plan_item_id` as `uuid`; `trading.plan_items.id` is a `bigint` identity (verified in `PlanConfigurations.cs`). Intents are referenced by `execution.intents.public_id` (uuid). Log ids: this group uses 6600 to 6699 (01: 6600-6614, 02: 6615-6634, 03: 6635-6644, 04: 6645-6659, 05: 6660-6674, 06: 6675-6699), the H2 group 6700 to 6799; existing ids end at 4791.
5. **Swing and partial-exit execution** (overnight stops, `PARTIAL_BE`, `PYRAMID`) are not in P12; a possible P12-13 after the exit, needing a product with persistent stops (Dhan forever orders, semantics UNVERIFIED) and a multi-leg reduce-only model.
6. **Doc and test pins to update with P12-01:** `CostArchitectureTests` project list, `LogEventIdTests.Shipped`, `LayerDependencyTests` arrays; docs 03 §4/§5, 05 §7, 08 §2, 17 §2/§3, 20 §A; `backend/CLAUDE.md` (second runtime role); `deploy/CLAUDE.md` (`--exec`, network `exec`).
7. **Fills to the journal.** Execution does not write portfolio or live tables: broker trades reach `portfolio.transactions` through the existing sync, P11-01's capture and P7-14's auto-journal; P12-06 adds a `journal_gap` cross-check (warn only). P11's human halts (`trading.live_halts`) are a read-only input to `no_halt`; `execution.halts` is the execution truth.
8. **Dedicated execution account.** Recommend (P11-05 or P12-11 note) using a Dhan account for P12 that the owner does not trade by hand on execution days.

## Done when
- [ ] The six slice specs and the H2 slice specs are Approved by the owner
- [ ] The ladder, the canary protocol and the cut line are accepted
- [ ] Findings 1 to 7 are in the roadmap or the slice specs

## Review fixes 2026-10-25
- D12 now states the BUY-only rule and the `canary_cap` check; the cut line no longer lists shorts; "may not" forbids automatic corrections.
- Slice table: build order 01..06, 07, 11, 08, 09, 10, 12; Needs and sizes follow H2's Size lines (group about 34.5 days); 34 checks, 17 halt triggers.
- Facts: compose profiles, log ids (existing end at 4801, P12 6600 to 6799). SEBI/static-IP record is P11-05's (`trading.compliance_records`), read by P12-11. Allocation check: P11-04A then P12-07.
- Finding 7 added (fills to journal, P11 halts as input).
