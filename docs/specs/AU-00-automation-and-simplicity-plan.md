# AU-00 — Automation and simplicity programme: the plan

- **Status:** Approved (owner, 2026-11-09, in chat). The owner asked for this programme, ahead of BK-00..BK-12, in chat on 2026-11-09.
- **Phase / roadmap row:** AU (new); order and dependencies below.
- **Design refs:** docs/design/04 §3, 09, 10, 11, 13, 16, 19 · ADRs 0240 to 0244

## Goal
The system recovers by itself after missed days, shows few settings, learns from a full simulation, and gives next-day suggestions behind one switch, while still placing no order.

## The owner's requirements
1. Recovery across missed days, in dependency order, started by system start, wake from sleep or broker reconnect, with one screen.
2. One Auto switch for the whole chain and next-day suggestions by basket or stock, with reason and evidence, on the P7 nightly plan.
3. Simulate everything: one job, all stocks, all history, training from it, and a report by stock or basket and date range, usable while it runs.
4. Simplicity, the most important: few visible settings, the rest behind good defaults and adjusted from what the system learned.
5. Remove unused code and functionality: audit first, then small reviewed slices.

Constraints: Auto only suggests and places no orders; P12 stays separate and owner-only. Learned changes may tighten automatically; loosening needs the owner's password. Simulation training is walk-forward, leaves the hold-out untouched, has no look-ahead and stays out of scorecard evidence unless it meets the existing rules. Only unreachable code that no spec plans to use is removed, and the list is recorded in an ADR first.

## Slices, in build order
| Id | Slice | Size (agent-days, uncalibrated) | Needs |
|----|-------|------|-------|
| AU-01 | Recovery planner (pure) | 2 | built services framework |
| AU-02 | Recovery runner and its three triggers | 3 | AU-01 |
| AU-03 | Multi-day data proof for the end-of-day service | 1 | AU-02 |
| AU-04 | Recovery screen | 2 | AU-02, AU-03 |
| AU-05 | Unused-code audit and the recorded removal list | 2 | none |
| AU-06 | Remove unused backend code, slice by slice | 1 to 3 | AU-05 |
| AU-07 | Remove unused frontend code and settings keys | 1 to 2 | AU-05, AU-06 |
| AU-08 | Settings inventory and visibility tiers | 2 | AU-05 |
| AU-09 | Simple settings screen | 2 | AU-08 |
| AU-10 | Learned tightening and password-gated loosening | 3 | AU-08 |
| AU-11 | Training-simulation record and evidence fence | 2 | built simulator, ADR 0207 |
| AU-12 | Simulate-everything job, resumable and yielding | 3 | AU-11 |
| AU-13 | Walk-forward training from the simulation | 3 | AU-11, AU-12 |
| AU-14 | Simulation report API | 2 | AU-11 |
| AU-15 | Simulation report screen | 2 | AU-12, AU-14 |
| AU-16 | Auto scope and reasoned next-day suggestions | 3 | built P7 pipeline |
| AU-17 | The Auto switch | 3 | AU-02, AU-10, AU-13, AU-16 |
| AU-18 | Auto and next-day suggestions screen | 2 | AU-16, AU-17 |
| AU-19 | AU programme check | 1 | AU-01 to AU-18 |

About 38 to 42 agent-days. After AU-19 the existing queue resumes: P3-09d, then BK-00..BK-12.

## What is reused (checked in the code on 2026-11-09)
- **Recovery:** `ServiceDefinition`, `ServiceConstraints.MustFollow`, `ScheduleCalculator.MissedOccurrence`, `CatchUpReach`, the scheduler's sleep detection (`ServiceScheduler`), `CompleteConnect`, `market.bar_gaps`.
- **Auto:** `nightly`, its six fail-closed gates, `SelectionPlanBuilder` reasons and basis text, `suggestions`, `OwnerAct`.
- **Simulation:** `research.sim_runs`, `RunSimulationHandler`, `LabHoldOutGuard`, `HoldOutStore`, `/api/reports/*` (already takes `from` and `to`), `BuildTrainingSet`, purged folds of ADR 0067, the challenger path of ADR 0220.

## Conflicts with earlier decisions, and how they are resolved
| # | Earlier decision | Conflict | Resolution |
|---|------------------|----------|------------|
| 1 | ADR 0018 §4 and P1-13 AC9: same day, one catch-up per start | Requirement 1 needs several days | ADR 0240 supersedes §4; the paused-never-catches-up rule stays |
| 2 | ADR 0086 §3: a published session is never re-run; a plan is for the next session | Requires "nightly plan" in the replay | Only the latest closed session, and only before its review cutoff; older ones are `superseded` or `not_recoverable`. No old plan is back-filled |
| 3 | ADR 0097: ten unattended scheduled sessions | Today a `catch_up` run writes `Manual=false` and would count | Recovered runs are flagged, do not count and break the streak (tightens) |
| 4 | ADR 0232 shadow writes; capture integrity (ADR 0225) | Replaying a decision-time capture would fabricate history | `TimeBound` class: shown as missed, never run later |
| 5 | P7 safety Q6: no automatic learning; P10 row: config changes only through the hypothesis loop | Requirement 4 adjusts from learning | ADR 0242 narrows both for tightening of allow-listed thresholds only, under doc 09 guardrails. Loosening still asks |
| 6 | Doc 09: one-click Revert | Reverting an automatic tightening loosens | Revert of that kind asks for the password |
| 7 | Owner rule 2026-10-23: auto defaults OFF | Requirement 4 implies automatic adjustment | Auto (default OFF, password on) is the one setting that also enables automatic tightening; no second switch |
| 8 | Doc 09 autonomy levels and P7-12 auto-approval | "Auto" could be read as approving tickets | Auto is unrelated: it does not change autonomy, and is pinned away from approval and arming (ADR 0244) |
| 9 | ADR 0207 locked hold-out, fixed at first Lab post | "All history" reaches into it | The simulation ends before the block; the report shows it locked (ADR 0243) |
| 10 | ADR 0207 trial ledger | A bulk run is a look | One `training_sweep` ledger row per strategy version |
| 11 | CF-11 scorecard; ADR 0146 separate bases | Training could leak as evidence | `purpose=training`, a separate basis, excluded per reader, no conversion path |
| 12 | ADR 0217, ADR 0220 | Training could influence decisions | Challenger only; all switches unchanged |
| 13 | ADR 0135, 0173, 0176, 0177 "no reader"; `NullPlanBuilder`, `MlInfluenceSwitchOff` | Look unused to a tool | Protected list in ADR 0241 |
| 14 | Default paused services | One switch must run them | Auto resumes the listed chain and restores exactly what it changed |

## Best-practice inputs (search snippets, primary texts not opened; UNVERIFIED beyond that)
- Backfills should be serial where a day depends on the previous, with bounded concurrency ([Astronomer, rerunning DAGs](https://www.astronomer.io/docs/learn/rerunning-dags), [Airflow FAQ](https://airflow.apache.org/faq.html)). It led to oldest-first order, one at a time, and a bounded reach.
- Purge and embargo around test folds, and purging does not fix selection bias ([Wikipedia, purged cross-validation](https://en.wikipedia.org/wiki/Purged_cross-validation)); López de Prado (2018) ch. 7 is cited there and was not read. It led to the embargo in AU-13 and the ledger row in AU-11.
- Defer secondary options to one subsidiary screen and label it specifically ([NN/g, progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/)). It led to one named Advanced level.

## Owner questions (defaults taken; the build proceeds unless told otherwise)
1. A recovered nightly breaks the ten-session streak. On a laptop that sleeps the P7 exit may take long; the strict reading is the default.
2. Auto ON also enables automatic tightening. A separate switch is possible but adds a setting.

## Unverified until the build measures it
Time and disk of the full simulation on the 10 GiB VM; whether each learning service is truly `Convergent` or `LatestOnly` (AU-01 records it per service, defaulting to `LatestOnly`).
