# Journey — the remaining specs, in order (index written 2026-10-24; reviewed and corrected 2026-10-25)

All files below are **Draft**: written under the owner's delegation ("do your own research for the best answers and write them into the specs"). Every design question was answered inside its spec under **Decisions (researched)** with reason, rejected alternative and source. An agent builds a slice only after the owner sets it **Approved**. SAFETY slices keep every new setting default OFF.

Sizes are agent-days, uncalibrated estimates (earlier phases overran). Total of the new specs: about **431 agent-days** (295 plus the system-baskets programme BK-01..BK-12, 51 days, plus the confidence programme CF-00..CF-12, 80 days) from the specs' own Size lines (P8 rest about 66, P3 leftovers 13, P7 leftovers 9, P9 33.5, P10 20, P11 20, P12 34.5, P13 to P15 about 97 as plans).

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
| System baskets (BK) | BK-00 plan; BK-01 index catalogue and fetcher (SAFETY), 02 derived engine, 03 derived job (SAFETY), 04 Baskets screen cards, 05 broker flags and F&O (SAFETY) = 20 d; BK-06 profile engine, 07 profile endpoints and screen (7 d), 08 clusters and gate breadth (SAFETY), 09 relative strength, 10 vol-targeted sizing (SAFETY), 11 universe cell dimension, 12 AI basket tools (SAFETY) = 33 d | 51 |
| Confidence programme (CF) | CF-00 plan 0.5; CF-01 effective n and power 4.5, 02 shrinkage 3.5, 03 DSR/PBO/block bootstrap 6.5, 04 history backfill to five years (SAFETY) 9.5, 05 cost stress and one-minute-later fill 5.5, 06 pre-registration, trial ledger, hold-out (light SAFETY) 6, 07 random-entry null and plateau 5.5, 08 shadow paper trading (light SAFETY) 6, 09 sequential failure test 3, 10 ML safety ladder and rollback (SAFETY) 13.5, 11 readiness scorecard (light SAFETY) 7, 12 decay monitor and kill rules (SAFETY) 9 | 80 |
| **Total** | | about 431 |

P8-00, P8-01, P8-05 and all P0 to P7 specs already existed, and P8-02, P8-06 and P8-15 are built.

## 2. Recommended order (the long poles first)

Wall-clock items cannot be sped up by agents, so they start first:
1. **P10 paper trading** (12+ weeks of real sessions): P10-01 and P10-05 early. It needs the P7 services switched ON by the owner and the P7 phase check (10 unattended sessions) running. P10-00 holds the owner's weekly routine (about 85 min a week).
2. **P7-16 depth recording** (6+ months before P13) and **P14-01 to P14-04 option-chain archive**: start in free capacity once the owner has checked disk and decided the allow-list entries.
3. **P11-05 static IP and SEBI runbook**: ISP lead time; ask Dhan the two open questions (below) early.
4. **P7-19 numeric levels** before P8-14, P12-08 and P12-09.

Build waves:
- **BK. System baskets** (specs BK-00..BK-12; fits beside waves A and B). **BK-01 and BK-02 early and in parallel (no shared files)**, because membership history only accrues from the first fetch (and the fetch needs the owner's terms-read and ADR first, section 5). Then BK-03, BK-05, BK-04 (BK-04 last so it shows every kind); then BK-06, BK-07 (BK-06 needs only P8-07, built); then BK-08 to BK-11 (BK-08 before P7 goes live on a basket gate; BK-11 before P6 cell reports grow; BK-10 needs BK-08); BK-12 last, after P9-05.
- **CF. Confidence programme** (specs CF-00..CF-12; fits beside waves A and B; research in `docs/research/confidence-*.md`). **First, in parallel (no shared files):** CF-01 (pure stats) and CF-04 Part A (daily bhavcopy and corporate actions; needs the owner's NSE terms ADR and allow-list act, section 5). **CF-08 shadow logging early** (after the P7 nightly plan runs and the owner switches it on): forward data only accrues from the first night it runs, so every week of delay is lost evidence. **CF-05 early** (cost is the largest false-edge source; needs only built P3/P5 parts). Then CF-02 and CF-03 **in order after CF-01** (all three edit `CellStatsCalculator`/`VerdictEvaluator`); CF-04 Part B (1-minute backfill, after the seam test design is approved); CF-06 (**needs CF-04**, else the hold-out makes every verdict inconclusive), then CF-07 (counts its looks in the CF-06 ledger) and CF-03 Part B (PBO). Then CF-09 (needs CF-08's series), CF-12, CF-10 (uses CF-12's paired bootstrap). **CF-11 last** (reads CF-01 to CF-10; a missing producer is `unavailable` and counts as fail).
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
| CF-08 shadow trades vs the P10 exit criterion (60 graded paper trades) | shadow candidates form a **separate edge count**; the P10 exit criterion is untouched (a change needs an ADR and the owner, P10-00 D6) | accept at CF-08 Approval |
| P0-13 / research note read the daily Dhan chart endpoint as allow-listed | **not allow-listed**: the list holds only `POST /v2/charts/intraday` (`BrokerEndpoints.DhanIntradayChart`); `/v2/charts/historical` needs the owner's allow-list act; CF-04 Part A uses NSE bhavcopy instead | owner (allow-list); correct the P0-13 reading |
| Research note and roadmap assume about 500 instruments | the owner tracks **214**, not 500; CF-04 sizes (requests, disk) use 214, with 500 as the upper bound (disk 2 to 4 GB, up to 9 GB) | none (sizes in CF-04) |
| CF-06/CF-07 plan a new trial ledger | `lab.test_ledger` **already exists**; CF-06 extends it (nullable columns, ADR 0045 insert-only) instead of creating a table | at CF-06 build |
| CF-01, CF-02, CF-03 all edit `CellStatsCalculator` / `VerdictEvaluator` | never two writers on the same file: run **in order CF-01, CF-02, CF-03** (or separate worktrees with a rebase) | build order |
| CF placeholders in CF-00 rows CF-09..CF-12 | corrected from the actual files on 2026-10-27 | done |
| Roadmap/ISS-22 "members as of today" | BK-01/03 store dated versions forward-only; no back-filled history | none |
| ADR 0005 "Proposed", names the eval "P9-07" | eval is P9-09 | accept ADR 0005 at P9-01 |
| `Microsoft.Extensions.*` pinned 10.0.12 vs `Microsoft.Extensions.AI` 10.11.0 needs 10.0.13+ | bump in P9-03 | pin change |
| P7-18 refuses `GET /v2/orders`; allow-list pinned | P11-01 changes the pin (owner's security act) | owner |
| `plan_item_allocations` paper-only in four places (ADR 0089) | P11-04A extends the check to `manual_live` first; P12-07 extends it to the other live modes, keeping `risk_scale` and `ramp_event_id` | owner decision |
| `CostArchitectureTests` pins the project list | P12-01 updates it on purpose | at build |
| P12-01 criterion 10 (grants) and `ExecSettingRegistry` home | amended in P12-01; P12-03 Soft-only rule amended in P12-03 | done in the files |
| ADR 0033 deviations (open Q8), ISS-24 etc. | settled in P3-01d | owner may revert |

Log event ids (existing code ends at 4801): P8-18 4900-4919, P8-19 4920-4939, P8-20 4940-4949, P8-23 4950-4952 (built), P11 5100-5159, P10 5200-5254, P9 6000-6199, P12 6600-6799; other P3/P7/P8 specs take the next free range at build and `LogEventIdTests` keeps them unique. ADR numbers are never reserved: "next free number at build time" (0102 onward: 0100 and 0101 are taken).

## 5. What only the owner can do (the specs cannot decide these)

- Approve each spec before it is built. Slices marked `Safety: yes` in their own header (at least P7-16, P7-20, P8-18 to P8-21, P9-01, P9-02, P9-03, P9-05, P9-07, P9-08, P9-10, P10-01, P10-02, P10-05, P11-01, P11-04, P11-05 and every P12-01 to P12-11) keep their settings OFF until a deliberate act. Every UI write that starts a job or changes research or book state needs a **fresh password per action plus a capability gate**; switching the assistant OFF needs no password.
- **System baskets (BK):** read the NSE terms (niftyindices.com, nseindia.com) and record the ADR **before** turning `Baskets:System:Fetch:Enabled` on (fetches stay OFF until then); confirm the eight pre-registered baskets; choose the benchmark (stay Nifty 50, or Nifty 500 once tracked); accept or change the thresholds of the 14 derived baskets; set each BK slice Approved.
- **Confidence programme (CF):** set the scorecard thresholds (CF-11 Q1, CF-01 Q4, CF-09 `mu1`, CF-10 cuts); choose the hold-out length (CF-06 Q1; `max(6 months, 20 %)` now, 12 months once 5 years exist); supply **five contract notes for Dhan charges** (three intraday, two delivery incl. a DP day; CF-05) so the charge sheet can be checked; read the NSE Data policy and file terms and record the **ADR** (as BK-01), then make the allow-list entry for the bhavcopy host(s) (and, if wanted, `/v2/charts/historical`) as a security act; confirm **disk 2 to 4 GB** (up to 9 GB) on the Podman volume; switch shadow paper (CF-08), backfill and every `Gate`/`enforce`/`kill.auto`/`auto_to_paper` mode on (all OFF by default; a `Gate` is a fresh-password owner act); set each CF slice Approved. SAFETY slices: CF-04, CF-06, CF-08, CF-10, CF-11, CF-12.
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

## 5c. Confidence programme: merged owner questions (from CF-00..CF-12)
Defaults are the specs' recommendations; each is the owner's call.
**Modes and thresholds.** (1) Approve modes `Off | ReportOnly | Gate`, section `Confidence`, defaults ReportOnly for display figures and Off for anything that can change a verdict (CF-00, CF-01 Q1). (2) Power 80 %, alpha 0.05 one-sided, target edge 0.15 R (or 0.20 R for display); evidence labels on n_eff: watch 30, usable 60 with lower bound above 0, scalable 150 (CF-00, CF-01 Q2/Q4). (3) May `Gate` later replace n by n_eff in gate 1 and in the evidence label (CF-01 Q3), and the label use the shrunk lower bound (CF-02 Q2)? (4) `TauSquaredFloor` 0.01 R^2 (CF-02 Q1); instrument-level pooling wanted, left out (CF-02 Q3). (5) DSR `MinProbability` 0.95 or 0.90; PBO limit 0.5; build CF-03 Part A now and Part B with CF-06 (CF-03 Q1-3). (6) Accept the CF-11 scorecard thresholds as a starting set; Gate R as the precondition of `Candidate -> Paper`; when `enforce` goes ON; `auto_to_paper` ever (recommended OFF); row 13 (benchmark) required for Gate S with no producer yet (CF-11 Q1-5).
**Data (CF-04).** (7) May Part B (Dhan 1-minute backfill, about 2,568 requests) run, after Part A and a probe pass? (8) NSE terms read and ADR; which hosts the allow-list gains (also section 5). (9) EQ only or also BE/BZ; whole market or tracked instruments (recommended whole market). (10) Hold-out length: same as (14) below. (11) Index and VIX daily: add `/v2/charts/historical` or build context from 1-minute index data (same as BK-00 Q3/P3-01d probe). (12) Disk 2 to 4 GB (up to 9 GB) acceptable. (13) Unresolved seams: prompt per instrument or the weekly Data quality list (recommended weekly list).
**Lab (CF-05 to CF-07).** (14) Hold-out `max(6 months, 20 %)` now and 12 months at 5 years; accept that nothing can PASS before CF-04 lands (3 folds); one shared hold-out for strategies and models (recommended); does a model inherit its strategy's trial count; when registration and hold-out enforcement go from report-only to blocking (CF-06 Q1-5). (15) Five contract notes, tier cuts (Rs 100 crore / Rs 20 crore) and bps (3, 8); drop tier C from edge claims or price it at 15 to 30 bps; when stress gates block (CF-05 Q1-4). (16) 95th percentile and rank 1 of 21 or looser (top 2 of 21) while data are short; 1000 seeds; 70 % plateau retention; when the three gates block (CF-07 Q1-4).
**Forward evidence (CF-08, CF-09).** (17) Shadow trades are a separate edge count and the P10 exit criterion stays as written; include reserve and fill-rejected candidates (recommended yes); switch shadow on now (default OFF, needs the nightly plan running); keep shadow rows forever (CF-08 Q1-4). (18) `mu1` 0.2 R (indifference 0.1 R net); caution 21 % and failure 5.3 % levels or failure only; line on the P10 phase tab or only the shadow summary (CF-09 Q1-3).
**ML and kill rules (CF-10, CF-12).** (19) 500-row cut and EPV 15; `P >= 0.95` as the bar; advance the hold-out start for a refit model (burns the old one); auto-rollback ON from the start or manual until seen twice; may a model influence anything before P12 (recommended advisory throughout) (CF-10 Q1-5). (20) Kill rules (three triggers, 2 times / 100 trades); `confidence.kill.auto` ever ON (recommended OFF until two proposals seen); is "no measurable difference" enough to stop retraining automatically or only to show it; learning curve weekly or nightly (CF-12 Q1-4).
Overlaps answered once: hold-out length (CF-04 Q4 = CF-06 Q1 = 10 = 14); daily index/VIX endpoint (11 = BK-00 and P3-01d allow-list acts); the NSE terms ADR (8 = BK-00 Q1, one ADR can cover both); `enforce`/`Gate` switch timing (6, 14, 15, 16 are one owner act per gate).

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
