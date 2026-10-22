# P5-00 — Strategy Lab v1: the plan, the split and the seam

- **Status:** Approved (owner, 2026-10-22, "approve all": the questions stand as recommended). Written 2026-10-22 from `main` at f4294e31, after P4-06 was built. Facts about the docs were read that day (doc 11, 16, 09 §gates, 03 §6, the roadmap P3 rows); the code was **not** re-read for this plan, so every statement about a type, a handler or a package is **UNVERIFIED against code** unless a built ADR says it. Sizes are agent-days, an estimate, not calibrated.
- **Phase / roadmap row:** P5 — Strategy Lab v1 (3 weeks, after P4). Row: "Lab API + Hangfire runs + SignalR progress; verdict & report; test ledger; `xd` CLI; Strategy Lab page (Monaco + schema); basket as test universe; `xd-sandbox` for C# plugins". Exit: "3 of your own ideas tested end-to-end without code changes".
- **Design refs:** docs/design/11-strategy-lab.md §1 to §6 · 09-learning-engine.md (qualification gates, lifecycle) · 16-configuration.md (`strategy_lab`, `qualification_gates`, `exit_walk_forward`) · 03-target-architecture.md §6 (Lab endpoints), §4 (`lab` schema, `xd-worker`, `xd-sandbox`) · ADRs 0034, 0036, 0037, 0041, 0045, 0053, 0054 · ISS-19 (2.1 years of data), ISS-24 (traps of the language).

## Goal
The owner types or uploads a strategy idea, the system validates it, runs it as a job over a basket, shows progress, and gives a verdict with a report and a ledger entry, without a code change and without a restart. Like P3 and P4, P5 first proves the machinery; the verdict is only as good as the data (about 2.1 years, no hold-out) allows, and every report says so.

## What exists and what does not (docs read 2026-10-22; code UNVERIFIED)
- **Exists (by the roadmap and the ADRs):** the rules language and compiler (P3-02, ADR 0034); the typed strategy model and the compile of its sections (P3-02b, ADR 0036); the simulator, fills, costs, swing, exits and overlays (P3-03 to P4-04); the run record, the `simulate` verb and the host-agnostic `RunSimulationHandler` that "moves behind the Lab API in P5" (P3-04a/b2, ADR 0041); the result store `research.sim_runs` / `sim_trades` (ADR 0045); the same-entries comparison and diagnostics (ADR 0053); the per-cell exit choice with folds (ADR 0054); the config keys `exit_walk_forward`, `qualification_gates`, `strategy_lab` (doc 16). SignalR hub `/hubs/events` exists since P2-10a (ADR 0024). A Hangfire reference exists in `XDrishti.Worker.csproj` (found by a grep); whether any job runs on it is UNVERIFIED.
- **Does not exist:** the strategy file (YAML, JSON Schema, hash: P3-02c); `lab.strategies` / `lab.strategy_versions` and the seed of the built-ins (P3-02d); the BUY to SELL mirror (P3-02e); the context job and `by_regime` (P3-01d); any `/api/lab/*` endpoint; `lab.lab_runs`, `lab.lab_results`, `lab.test_ledger`; a verdict, a robustness block (sensitivity, Monte Carlo, random baseline), a report; the `xd` CLI (UNVERIFIED: the repo has the `simulate` verb, not an `xd` binary); any Lab page; `xd-sandbox`.
- **Prerequisites listed as "before P5" in the roadmap and still future:** P3-02c (3 d), P3-02d (2 d), P3-02e (3 d), P3-01d (3.5 d), P3-04d (cards, only if wanted). They are **not** P5 slices; they are in the sizes below only as a dependency line. Without P3-02d a Lab run can only name a built-in; without P3-02c there is no text the owner can type; so P5-01 cannot start before them.

## The seam (the decision the slices share)
**Fact (ADR 0041, 0045).** `RunSimulationHandler` is host-agnostic; `simulate --store` writes a run into `research.sim_runs`; the roadmap already says a later `lab.lab_runs` row "points at a run key, so the Lab never copies trades".

**Recommended design (D1).** The Lab is a thin orchestration layer over three existing pure pieces: the compile (`StrategyCompiler`), the run (`RunSimulationHandler` and the P4-05/P4-06 comparison and selection), and a **new pure verdict evaluator** (gates in, PASS/MARGINAL/FAIL and per-gate reasons out; no clock, no I/O). A `LabRunner` application service executes a *Lab run* as a fixed sequence of stages (validate, compile, quick test, full test, robustness, verdict) and reports each stage through one `ILabProgress` port. The API enqueues; the Hangfire job in `xd-worker` calls the same `LabRunner`; the `xd` CLI calls it in-process. So one code path serves the page, the CLI and the tests, and the stages can be built and approved one at a time (a run that stops after "quick test" is a legal v0 run).

Alternatives: (a) the page calls the verbs through a shell — rejected, no progress, no typed errors; (b) a second engine path for the Lab — rejected, breaks "run by the same engine as built-in strategies" (doc 11 §1); (c) chosen.

**D2. A run's identity.** A Lab run is `(strategy version hash, universe, window, test method, config snapshot hash, engine version)`; `lab.lab_runs` stores that plus the run keys of the simulation runs it produced. The same input twice returns the stored result (idempotent, like ADR 0045). UNVERIFIED: the exact key text; P5-02 fixes it.

**D3. The ledger counts variants from the first Lab run.** Every Lab run of a strategy family appends a ledger row (family, hash, parent hash, universe, window, method, outcome). The thresholds tighten with the count (doc 11 §5). P3 and P4 runs outside the Lab are not retro-fitted (they carry the strategy hash, so a count can be reconstructed: owner question 6).

**D4. Honest limits are part of every verdict.** No hold-out and about 2.1 years (ISS-19): the report prints the data range, the number of folds, "in-sample on the data shown" where it is true, and cannot print PASS when fewer folds exist than `min_positive_folds_pct` can mean something for (a rule in the evaluator, owner question 3).

## Slices (proposed; sizes in agent-days, an estimate)
| Slice | Content | Size | Needs |
|---|---|---|---|
| **P5-01** | **Lab API: validate and the strategy library.** `POST /api/lab/validate` (text in, located problems out, no state), `POST/GET /api/lab/strategies` and versions (create a family, add an immutable version, list, download as a file, clone with a parent), over `lab.strategies` / `lab.strategy_versions` of P3-02d; the JSON Schema served for the editor. | 2 to 2.5 | P3-02c, P3-02d |
| **P5-02** | **Lab runs: the job, the run record and the quick test.** `lab.lab_runs`, `POST /api/lab/runs` (strategy version, basket or symbols, window, method `quick` first), the `LabRunner` with stages validate, compile and quick test (1 year, 30 symbols by doc 16), Hangfire job in `xd-worker`, SignalR progress on `/hubs/events`, `GET /api/lab/runs/{id}`, cancel, idempotency by D2, basket as test universe. | 3 to 3.5 | P5-01, P3-04a, P3-10 |
| P5-03 | **Test ledger and the gates.** `lab.test_ledger`, the family count, the multiple-testing adjustment of the thresholds (`multiple_testing_adjustment`), the pure verdict evaluator with the `qualification_gates` of doc 16, per-gate reasons. | 2.5 | P5-02 |
| P5-04 | **Full test.** Walk-forward over timeframe, side, exit using the P4-05 comparison and P4-06 selection, costs on, OOS-only PASS, `full_period` marked exploration; results in `lab.lab_results`. | 3 | P5-02, P5-03 |
| P5-05 | **Robustness.** Parameter sensitivity (a sweep of at most four params, each point a ledger entry), Monte Carlo (seeded), by year, `by_regime` only if P3-01d is built, random-entry baseline. | 3 | P5-04 |
| P5-06 | **Verdict and report.** The report of doc 11 §4 as JSON plus a static HTML page and a downloadable folder (reusing the export library of ADR 0041), plain-English summary from report numbers only (assistant optional). | 2 | P5-05 |
| P5-07 | **`xd` CLI.** `xd strategy test file.yaml`, `xd strategy validate`, `xd lab runs`; in-process `LabRunner`, exit codes; UNVERIFIED how `xd` is packaged today. | 1.5 | P5-02 |
| P5-08a | **Strategy Lab page, editor and validate.** Monaco YAML with the served schema, live problems, library list, versions, clone, upload. | 2.5 | P5-01 |
| P5-08b | **Strategy Lab page, runs and report.** Start a run, progress (SignalR), verdict and report screens matching `docs/prototype/`, the ledger view. | 3 | P5-02, P5-06, P5-08a |
| P5-09 (optional) | **`xd-sandbox` for C# plugins.** No network, read-only data mount, CPU and memory limits, `IStrategy` contract. | 4 or more | P5-04 |

Sum without P5-09: about 23 to 25 agent-days; with the prerequisites P3-02c, d and e about 8 more. That is more than "3 weeks" can hold with the owner's checks (the same overrun P4-00 recorded).

## Scope
**In:** P5-01 to P5-08b above; the basket as a test universe (in P5-02); the ledger and the gates; the CLI; a screen that matches the prototype.
**Out (not in P5):** C# plugins and `xd-sandbox` (P5-09, optional, recommended later); promotion to Candidate, Paper, Active (P6: the page shows a disabled button, the assistant cannot promote); the form builder and plain-English drafting (doc 11 §1, the assistant, later); regimes in cells (P6); per-trade tuning (P7); the nightly use of a Lab strategy (P7); events and results calendar (P15); a hold-out data policy and the Dhan backfill toward 5 years (ISS-19, a precondition for trust, not a slice).

## Acceptance criteria (of the phase; each slice carries its own list)
1. A strategy typed in the Lab page (or uploaded, or sent by `xd strategy test`) is validated with located problems, stored as an immutable version, run on a basket as a job with progress, and ends in a verdict and a report, with no code change and no restart.
2. The same strategy text, universe, window, method and engine version give the same result and the same run key whether started by the page, the API or the CLI (D1, D2).
3. A Lab run of a built-in strategy equals the `simulate` run of the same input, trade for trade (the Lab adds no second engine).
4. Every Lab run appends a ledger row; the thresholds of the verdict tighten with the family count; an edit is a new version and a new variant (D3).
5. Only walk-forward out-of-sample results can PASS; `full_period` is labelled exploration; fewer folds than the evaluator's minimum cannot give PASS (D4).
6. The assistant path cannot promote (no endpoint accepts it from the assistant identity; UNVERIFIED how identities exist).
7. No secret in logs or run records; a run does not read outside `data/` and the database; the sandbox (if built) has no network.
8. Phase exit by the owner: 3 of his own ideas tested end-to-end without a code change, with the honest-limits lines present in each report.
9. Every slice ends with `scripts/check.sh` green, `scripts/gen-api.sh` for every API change, a `reviewer` pass, ADRs from 0055 on (UNVERIFIED next free number) and `scripts/build-docs.sh`.

## Contract
- **API:** doc 03 §6 lists `POST /api/lab/strategies`, `POST /api/lab/validate`, `POST /api/lab/runs`, `GET /api/lab/runs/{id}`, `POST /api/lab/runs/{id}/promote`. This plan adds the reads (`GET` strategies, versions, runs list, report, ledger) and cancel; `promote` stays out (P6). Details in each slice.
- **Data:** schema `lab`: `strategies`, `strategy_versions`, `lab_runs`, `lab_results`, `test_ledger` (doc 03, doc 11 §6). Insert-only for `xd_app` where the content is a record (versions, ledger, results); a run row may change state. One migration per slice that adds tables.
- **UI:** the Lab page (P5-08a/b), routes under `/lab`.
- **Events/jobs:** one Hangfire queue `lab` with `max_parallel_runs: 2` (doc 16); progress on the existing hub; a run survives an `xd-worker` restart as "interrupted", never silently half-done.

## Non-functional
Quick test 1 to 2 minutes (doc 11 §3); full walk-forward 10 to 60 minutes (doc 11 §3, UNVERIFIED on this data: the timed run of 214 instruments is still open). Deterministic by seed. The API never blocks on a run. Logs carry the run id, stage and counts, never a strategy text beyond its hash.

## Risks
1. **The prerequisites.** Four P3 slices (about 11.5 d) are "free capacity" and unapproved; P5-01 stalls until P3-02c and P3-02d are built.
2. **A verdict on 2.1 years.** With 4 to 6 folds a PASS is weak evidence; the ledger count and the gate rules must not make it look strong.
3. **Cost of a full test.** N candidates over a basket for every sweep point may not fit "10 to 60 minutes"; the first timed run decides (P5-04).
4. **Frontend size.** Monaco with a schema, a progress stream and a report are the largest unknowns (UNVERIFIED that the frontend has a bundle budget that Monaco fits).
5. **Hangfire and SignalR from the Worker.** The job runs in `xd-worker`, the hub lives in `xd-api`; how progress crosses the two is UNVERIFIED (Postgres `LISTEN/NOTIFY` per doc 03 §4 is the design).
6. **Test-until-it-passes.** Mitigated by D3, but the owner remains the weakest link; the report shows the variant count beside the verdict.

## Test plan (of the phase)
A seeded end-to-end Lab run on fixtures per slice; the Lab-equals-`simulate` check (criterion 3); the verdict evaluator by hand tables; ledger tightening by a property over variant counts; API integration on Testcontainers; frontend by MSW and a progress stream stub; architecture tests for the new `Lab` module boundaries.

## Open questions (for the owner; each with a recommendation)
1. **Order and the prerequisites.** Build P3-02c and P3-02d first (5 d), then P5-01 and P5-02 as the first two slices? Recommended: yes; P3-02e (the mirror) and P3-01d can follow, they only gate user SELL sides and `by_regime`.
2. **The first run before the verdict.** Ship P5-01 and P5-02 alone (a quick test with no verdict) and use them for a week before P5-03? Recommended: yes; it shows what the owner needs from the verdict.
3. **PASS on few folds.** May the evaluator refuse PASS (cap at MARGINAL) while fewer than 6 folds exist? Recommended: yes until ISS-19 is resolved.
4. **Hold-out.** Keep the last months out of every Lab run until the verdict? Recommended: yes for the full test, but it costs data on 2.1 years; decide with the backfill (ISS-19, Q7).
5. **`xd-sandbox` (P5-09).** In P5 or later? Recommended: later; the DSL covers the three ideas of the exit criterion and the sandbox is the riskiest slice.
6. **The ledger before P5.** Seed it from the stored P3/P4 runs by strategy hash? Recommended: no; start at the first Lab run and say so on the page.
7. **The page.** One slice or two (P5-08a/b)? Recommended: two, so the editor is usable before the report exists.
8. **Cut line.** If P5 runs late, cut in this order: P5-09, the form of the HTML report (keep JSON), P5-07 CLI, the Monte Carlo in P5-05. Never the ledger, the OOS-only rule or the honest-limits lines. Recommended: accept.
9. **Next ADR numbers and the `xd` binary.** Confirm that ADRs continue at 0055 and how `xd` is packaged (UNVERIFIED). Recommended: read before P5-07.

## Done when
- [ ] The owner has answered questions 1 to 9 and the status is Approved
- [ ] P5-01 and P5-02 are Approved and built; the other slices have specs when their turn comes
- [ ] The phase criteria have evidence; `scripts/check.sh` green; reviewer: no blockers/majors
- [ ] Docs 03, 11, 16 and the roadmap updated; `scripts/build-docs.sh` run
