# Journey — the remaining specs, in order (index written 2026-10-24; reviewed and corrected 2026-10-25)

All files below are **Draft**: written under the owner's delegation ("do your own research for the best answers and write them into the specs"). Every design question was answered inside its spec under **Decisions (researched)** with reason, rejected alternative and source. An agent builds a slice only after the owner sets it **Approved**. SAFETY slices keep every new setting default OFF.

Sizes are agent-days, uncalibrated estimates (earlier phases overran). Total of the new specs: about **348 agent-days** (295 plus the system-baskets programme BK-01..BK-12, 51 days) from the specs' own Size lines (P8 rest about 66, P3 leftovers 13, P7 leftovers 9, P9 33.5, P10 20, P11 20, P12 34.5, P13 to P15 about 97 as plans).

**Built while these were written (2026-10-25):** the parallel build session merged **P8-02** (Learning screen), **P8-06** (report endpoints, ADR 0100) and **P8-15** (plan notices, ADR 0101) from their own Approved specs `P8-02-learning-screen.md`, `P8-06-report-read-endpoints.md`, `P8-15-plan-notifications.md`. The Drafts with the longer names for those three are marked SUPERSEDED and are not counted. Downstream Drafts were re-aligned to the built shapes.

## 1. What exists now

| Phase | Specs (all in this folder) | Days |
|---|---|---|
| P8 reports & UI (rest) | P8-03, 04, 07, 08, 09, 10, 11, 12, 13, 14, 16, 17; SAFETY: **P8-18** tweak and re-run, **P8-19** owner-act forms, **P8-20** exports, **P8-21** PWA; P8-22 audit screen; plus the new small slices P8-02b (extract `HeatGrid` and the tab registry, 0.5 to 1 d) and P8-15b (carry the P7-20 items, 1 d) | about 66 |
| Leftovers P3 | P3-01d context data (index, VIX), P3-01e breadth, P3-02e BUY to SELL mirror, P3-02f lag and never-true lint, P3-04d decision trace on cards | 13 |
| Leftovers P7 | P7-16 depth recording (SAFETY), P7-19 numeric ticket levels, P7-20 price-crossing alerts and post-trade prompts | 9 |
| P9 assistant | P9-00 plan, 01 xd-llm, 02 xd-mcp read-only, 03 endpoint and `ai.*`, 04 chat UI, 05 data tools, 06 briefing, 07 strategy drafting, 08 proposals, 09 eval set, 10 Hermes PoC (sizes: P9-05 5 d, P9-09 5 to 6 d) | 33.5 |
| P10 paper | P10-00 plan, 01 paper fill capture, 02 ledger and open positions, 03 weekly review and discipline, 04 backtest vs paper, 05 config governance, 06 phase check (sizes: P10-03 5 d, P10-04 4 d) | 20 |
| P11 small live | P11-00 plan, 01 manual-live fill capture, 02 execution feedback, 03 live vs paper slippage, 04 scale-up and risk ramp, 05 static IP and SEBI runbook, 06 phase check | 20 |
| P12 execution | P12-00 plan (build order 01 to 06, then 07, 11, 08, 09, 10, 12), 01 state machine, 02 Dhan adapter and sandbox, 03 pre-trade checks, 04 broker-side stops, 05 kill switch, 06 order stream and reconciliation, 07 modes, 08 semi-auto, 09 auto-live, 10 screens, 11 daily login and static IP ops, 12 phase check | 34.5 |
| P13 to P15 (plans) | P13-00 tick and depth (10 slices, about 23 d), P14-00 options and stat-arb (17 slices, about 44.5 d), P15-00 fundamentals and events (12 slices, about 30 d) | about 97 |
| System baskets (BK) | BK-00 plan; BK-01 index catalogue and fetcher (SAFETY), 02 derived engine, 03 derived job (SAFETY), 04 Baskets screen cards, 05 broker flags and F&O (SAFETY) = 18 d; BK-06 profile engine, 07 profile endpoints and screen (7 d), 08 clusters and gate breadth (SAFETY), 09 relative strength, 10 vol-targeted sizing (SAFETY), 11 universe cell dimension, 12 AI basket tools (SAFETY) = 33 d | 51 |
| **Total** | | about 348 |

P8-00, P8-01, P8-05 and all P0 to P7 specs already existed, and P8-02, P8-06 and P8-15 are built.

## 2. Recommended order (the long poles first)

Wall-clock items cannot be sped up by agents, so they start first:
1. **P10 paper trading** (12+ weeks of real sessions): P10-01 and P10-05 early. It needs the P7 services switched ON by the owner and the P7 phase check (10 unattended sessions) running. P10-00 holds the owner's weekly routine (about 85 min a week).
2. **P7-16 depth recording** (6+ months before P13) and **P14-01 to P14-04 option-chain archive**: start in free capacity once the owner has checked disk and decided the allow-list entries.
3. **P11-05 static IP and SEBI runbook**: ISP lead time; ask Dhan the two open questions (below) early.
4. **P7-19 numeric levels** before P8-14, P12-08 and P12-09.

Build waves:
- **BK. System baskets** (specs BK-00..BK-12; fits beside waves A and B). **BK-01 and BK-02 early and in parallel (no shared files)**, because membership history only accrues from the first fetch (and the fetch needs the owner's terms-read and ADR first, section 5). Then BK-03, BK-05, BK-04 (BK-04 last so it shows every kind); then BK-06, BK-07 (BK-06 needs only P8-07, built); then BK-08 to BK-11 (BK-08 before P7 goes live on a basket gate; BK-11 before P6 cell reports grow; BK-10 needs BK-08); BK-12 last, after P9-05.
- **A. P8 exit path** (P8-00 cut line): P8-06 is built, so next **P8-02b** (extract `HeatGrid` and the tab registry that P8-03/08/09/10/12/13 assume), then P8-07, 08, 09; P8-03/04 in parallel worktrees (small merge in `router.tsx`/`navigation.ts`); then P8-10 to P8-14 and P8-15b; P8-16 per screen; P8-17 last. P8-19, 20 and 21 depend only on P8-18 Part A (the 1 d capability gate), not on its job half.
- **B. Before P6/P7 features go live:** P3-01d probe, then P3-01d, P3-01e (regime and market gate), P3-02e, P3-02f part A, P3-04d.
- **C. P9:** P9-01 then P9-02 (both edit `compose.yaml`/`xd-up.sh`: not parallel), P9-03, P9-05, P9-04, then P9-06 to P9-09, P9-10 last (default verdict: reject).
- **D. P11:** P11-05, P11-04a, P11-01, P11-02, P11-04b, P11-03, P11-06.
- **E. P12:** P12-01 to P12-06, then P12-07, P12-11, P12-08, P12-09, P12-10, P12-12 (readiness P12-11 is needed by 08 and 09); exit is wall-clock (P11 at least 8 weeks, then 10 assisted and 30 semi-auto sessions).
- **F. P13 to P15:** each plan lists its slices; turn them into full specs when the previous phase's evidence exists.
- SAFETY slices P8-18 to P8-21 wait for the owner's Approved; they are not on the exit path.

## 3. Extra slices the reviews found (no spec file yet; each is a one-paragraph addition to the named plan)

| Slice | Why | Days |
|---|---|---|
| Ticket-state bridge (P12-00 finding 1) | nothing writes a plan ticket's `Triggered`/`Closed` once orders exist (also P10: `plan-expiry` expires Armed tickets before EOD) | 1.5 |
| Lift `simulation.needs_context` / `needs_expiry_days` (P3 follow-up) | the simulator refuses context and expiry-day strategies; needs a context input and adapter | 2 |
| P7-16b trade-tick recording | P13 aggressive-volume feature has no source | 2 |
| P3-01d0 probe as its own slice; P3-01d2 daily Dhan client; P3-01d3 daily CSV loader | conditional on the probe outcome | 0.5 / 1.5 / 1.5 |
| P8-10b read of `research.exit_selections` | the strongest "which exit" evidence | 2 |
| P8-02b extract `HeatGrid` to `shared/analytics` and a tab registry | P8-03/08/09/10/12/13 assume them; the built P8-02 has `Heatmap.tsx` and a hard-coded tabs array | 0.5 to 1 |
| P8-15b carry the P7-20 ticket notices as extra items of the built `GET /api/plans/notices` | route only, no command; the snooze/dismiss/mute POSTs live on the plan screen | 1 |
| Write the charge-sheet confirmation (owner act) | ADR 0100 defers it; until it exists no report or paper figure can be printed net (P8-06, P10-02) | 1.5 |
| P10-07 per-cell paper facts for the P6-08 Paper to Active gate | mostly `insufficient` | 2 |
| P12-13 swing, partial-exit and pyramid execution | out of P12 | UNSIZED |
| Order-group concept for two-leg pairs (P12) | before any live `PAIRS` | UNSIZED |

Suggested splits when a slice overruns (all 3 d or more): P8-06 (A spike, reader, runs / B endpoints), P8-10 (a basics / b exit comparison and Monte Carlo), P8-18 (gate 1 d / job 3.5 d), P8-19 (backend 1.5 / forms 2.5), P9-03 (03a tables, settings, reads / 03b chat, loop, grounding), P9-07, P9-09, P11-01 (trade-book / order-book halves), P11-04 (04a live enabling / 04b gates and halts), P12-07 (assisted sheet 07b), P12-08 (08a intent creator / 08b ticket and tap).

## 4. Where the specs disagree with the design docs or the roadmap (resolve at Approval)

| Item | Resolution in the specs | Action |
|---|---|---|
| Doc 03/04 `ops.audit_log`, doc 03 §5 `trading.orders`, doc 03 §6 `/api/suggestions`, docs 03/17 read-only DB role, docs 03/05 `bars_1m_live` | none exists; audit is six stores (P8-22), orders live in schema `execution`, plan suggestions at `/api/plans/{id}/suggestions`, P9-02 creates two read-only roles, nothing builds live bars (ADR 0024) | design-doc edits + ADR at build |
| Roadmap P10 "paper/minimal size"; P12 "6 weeks"; P8-04 "no backend change"; P8-06 endpoint list; P3-01e 2 d, P3-04d 1.3 d | paper only (P7 decision); P12 exit is wall-clock (months); P8-04 adds two GETs and amends ADR 0080; `what-works` added; sizes 2.5 d and 2 d | roadmap rows |
| Doc 09 §11 / doc 13 R30 "weekly" counterfactual | windows are quarterly (P8-12) | design change |
| Roadmap "Monte Carlo 90th pct" for P10 | Lab stores P50/P95 at the backtest N; P10-04 uses a seeded bootstrap at paper n | accepted in P10-04 |
| `max_hold_sessions` 1 to 5 vs PEAD weeks | P15-09 proposes 20 for event families | design change + ADR |
| Doc 06 §2.1 options skipped; doc 09 seventh cell-key part `expression` | P14 plan; BK-11 takes the **7th** part (`universe`), so P14's `expression` becomes the **8th** (P14-00 edited; `TryParse` accepts 6, 7 or 8) | design change + ADR |
| P8-07 Decision 5 (one benchmark, ADR 0102) vs BK-06 "difference to the index" | BK-06 shows basket minus index as a difference, not as alpha; the one app-wide benchmark stays | accept wording at BK-06 Approval |
| P9-05 schema budget 2,200 tokens | BK-12 raises it to 2,300 (or one older description is cut) | owner (BK-12 Q1) |
| Basket entity has no `kind`/`system` | BK-01 adds both (`kind` in manual, portfolio, index, list, derived); ADR 0014 protection reused | at BK-01 build |
| Roadmap/ISS-22 "members as of today" | BK-01/03 store dated versions forward-only; no back-filled history | none |
| ADR 0005 "Proposed", names the eval "P9-07" | eval is P9-09 | accept ADR 0005 at P9-01 |
| `Microsoft.Extensions.*` pinned 10.0.12 vs `Microsoft.Extensions.AI` 10.11.0 needs 10.0.13+ | bump in P9-03 | pin change |
| P7-18 refuses `GET /v2/orders`; allow-list pinned | P11-01 changes the pin (owner's security act) | owner |
| `plan_item_allocations` paper-only in four places (ADR 0089) | P11-04A extends the check to `manual_live` first; P12-07 extends it to the other live modes, keeping `risk_scale` and `ramp_event_id` | owner decision |
| `CostArchitectureTests` pins the project list | P12-01 updates it on purpose | at build |
| P12-01 criterion 10 (grants) and `ExecSettingRegistry` home | amended in P12-01; P12-03 Soft-only rule amended in P12-03 | done in the files |
| ADR 0033 deviations (open Q8), ISS-24 etc. | settled in P3-01d | owner may revert |

Log event ids (existing code ends at 4801): P8-18 4900-4919, P8-19 4920-4939, P8-20 4940-4949, P11 5100-5159, P10 5200-5254, P9 6000-6199, P12 6600-6799; other P3/P7/P8 specs take the next free range at build and `LogEventIdTests` keeps them unique. ADR numbers are never reserved: "next free number at build time" (0102 onward: 0100 and 0101 are taken).

## 5. What only the owner can do (the specs cannot decide these)

- Approve each spec before it is built. Slices marked `Safety: yes` in their own header (at least P7-16, P7-20, P8-18 to P8-21, P9-01, P9-02, P9-03, P9-05, P9-07, P9-08, P9-10, P10-01, P10-02, P10-05, P11-01, P11-04, P11-05 and every P12-01 to P12-11) keep their settings OFF until a deliberate act. Every UI write that starts a job or changes research or book state needs a **fresh password per action plus a capability gate**; switching the assistant OFF needs no password.
- **System baskets (BK):** read the NSE terms (niftyindices.com, nseindia.com) and record the ADR **before** turning `Baskets:System:Fetch:Enabled` on (fetches stay OFF until then); confirm the eight pre-registered baskets; choose the benchmark (stay Nifty 50, or Nifty 500 once tracked); accept or change the thresholds of the 14 derived baskets; set each BK slice Approved.
- Security acts: allow-list entries (index/VIX history, option chain, expired options, depth, `GET /v2/orders`), switching services ON, the private-tunnel phone path (P8-21), any live mode promotion with password (P12-07).
- Facts only Dhan or the ISP can give: does static IP apply to data calls or only orders; whether an under-10-orders/second personal algo needs registration or an algo tag (sources disagree); sandbox behaviour; ISP static IP.
- Evidence only wall-clock gives: 60 graded paper trades, 8+ live weeks, 30 semi-auto sessions, the owner's stopwatch for P8-17, labelling 200 announcements (P15), confirming the charge sheet (ISS-25), the 5-year backfill (ISS-19) that options and PEAD need.
- Recommendation noted in P12-00: a **dedicated Dhan account for execution**, so manual trades do not cause reconciliation breaks.

## 5b. System baskets: merged owner questions (from BK-00 and BK-06..BK-12)
BK-00: (1) May the system fetch NSE index files (terms read, ADR; until then OFF)? (2) Confirm the eight pre-registered baskets. (3) Track all Nifty 500 members so derived universes are market-wide (about 500 instruments of history)? (4) Benchmark: stay Nifty 50 or move to Nifty 500 once tracked? (5) Dhan MIS/MTF lists as baskets now (BK-05) or only in P12? (6) Accept the numeric thresholds of the 14 derived baskets as pre-registered, or change them before build?
BK-06/07: (7) Default index for broad baskets (same as 4; recommended one app-wide benchmark). (8) Is "difference to the index" acceptable wording? (9) Route `/baskets/:basketId/profile` or under Reports (`/reports/baskets/:id`)?
BK-08: (10) Gate breadth basket: Nifty 50 or the liquid-100 derived basket (recommended liquid-100 once dated, else Nifty 50)? (11) Default `max_risk_per_cluster_pct` (recommended 40 % of total open risk limit; the number is the owner's). (12) Tightening without the password (CLI only)? Recommended no.
BK-09: (13) Pre-registered rank universes (recommended Nifty 100 and liquid-100; F&O third). (14) Test `RS_ROTATION` only after 6 months of dated membership, or build now with the survivorship label (recommended build now, Lab study later)?
BK-10: (15) Down-scaling only, or also study up-scaling (recommended not now)? (16) `sizing.vol_target.enabled` default OFF (recommended) or ON? (17) Min or product when the gate is reduced? (18) Capacity default 1 % of ADV20? (19) Settings form (BK-10b) in the same wave or API plus CLI first?
BK-11: (20) Is liquidity the right single dimension (vs volatility tiers or F&O split)? (21) Terciles by count (recommended) or fixed top-100 / next-200 / rest? (22) Which eight baskets are pre-registered (same as 2)?
BK-12: (23) Token budget 2,300 (from 2,200) acceptable? (24) May the assistant quote the BK-10 sizing study (recommended out)?
Duplicates (2 and 22; 4 and 7) are answered once.

## 6. Honest limits of this journey
- Specs were checked against the repo by reading; nothing was built or run. Anything not opened is marked UNVERIFIED in the spec.
- External facts (Dhan API, SEBI, NSE, library versions) were read on 2026-10-24; primary SEBI/NSE texts could not be opened, so those items are UNVERIFIED and carry a re-check step in the spec.
- Many specs run 15 to 29 KB, above the 8 to 14 KB guide, because they carry hand-worked tables and decisions. Trim at Approval if wanted.
- Three independent reviews on 2026-10-25 found 2 blockers in P9 (stored text steering an auto-run; auto-accept starting a backfill), 3 in P12 (an automatic corrective BUY, shorts flagged oversold, an unsatisfiable boundary test) and about 40 majors/minors; the fixes were sent back to the authors and are recorded at the end of each changed spec under 'Review fixes 2026-10-25'. Reviewers did not read every file: they skipped P9-04/05/06/09, P10-03/04/06, P11-00 to 03/06, P12 beyond the safety paths, P13 to P15, and the P3 leftovers. Spot-check those at Approval.
- A parallel build session works on `main` in this checkout (worktrees under `.claude/worktrees`) and merged drafts of this folder into commits; commit or move these files soon to avoid merge friction.

## 7. Review fixes round 2 (2026-10-25)
A second review round was applied to the Drafts; each changed spec has a 'Review fixes 2026-10-25 (2)' section at its end. All stay Draft.
- **P3-04d:** the new export format is `xd-sim-export-4` (3 is already the P4-01 legs); ORB has only a trigger (criteria 1-2 reworded); the log id is the next free one at build; `InstrumentInputReader` extends the card handler's own reader and the run handler's moves onto it.
- **P3-01d / P3-01e:** `NSE:INDIA_VIX` cited to doc 06; no table-level UPDATE/DELETE, column grants only by P3-01e; breadth gets `BreadthGraceSessions` so a late Dhan load does not freeze `insufficient`; criterion 1 fixture states `MinVoters` 5.
- **P3-02e / P3-02f:** mirror row for a subscript over a call (golden `close > highest(20)[1]` -> `close < lowest(20)[1]`), scalar `gap_pct` operator flip stated; P3-02f Part B depends on that row, `NR7_ID_BO` uses about seven `high[k]-low[k]` terms.
- **P13-00:** NOT_EVALUABLE (not INCONCLUSIVE) at 5 folds; history need 10 months, earliest evaluable 2027-10-01; the two extension tables move to the P13-05 shared-core migration; `BrokerEndpoints` and `PlanPhaseCheck.PinnedAllowList` change in P7-16's commit.
- **P14-00:** allow-list rows change `PlanPhaseCheck.PinnedAllowList` in the same commit (ADR 0097); fewer than 6 folds is NOT_EVALUABLE; the 5-year backfill adds about 59 M rows (about 100 M total).
- **P15-00:** PEAD floor is 250 events and PEAD is NOT_EVALUABLE on this universe by construction (12-quarter burn-in, 32 seasons needed against 20); dependency wording fixed; `not_event_day` changes the `filter_not_available` refusals, the engine version (ADR 0037) and the run identity.
- **Sizes:** P9-05 5 d, P9-09 5 to 6 d, P10-03 5 d, P10-04 4 d applied to the table: P9 33.5, P10 20, total about 295.
