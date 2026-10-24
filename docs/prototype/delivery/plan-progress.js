/* xDrishti delivery plan — progress snapshot (what is built, where we are, what is next).
   Source of truth: docs/design/18-roadmap.md, docs/specs/, docs/adr/. Keep this file in sync when a slice lands:
   change a slice's `s` (built | draft | later | blocked | optional), add its ADR, bump asOf.
   This view is the real state of the build; the Backlog/Board story ticks (plan-data-*.js, last synced 2026-10-09)
   are the original product-level plan and are not updated per slice. */
(function () {
  window.XDP = {
    asOf: "2026-10-24",
    now: "P0–P6 built (P6 bar regime labels, which wait for the market-context job P3-01d). P7 is built except pre-open/arming (P7-11), the review screen (P7-17) and the phase check (P7-18); the nightly and weekly services start paused, so no plan is produced until you switch them on. Nothing places an order.",
    owner: [
      "Run your own 3 strategy ideas through the Lab (P5 exit criterion) and train + register a model on your stored runs (`xd learn model`, `xd learn experiment`): `xd learn phase-check` reads NOT EVALUABLE until then.",
      "Decide the data question (ISS-19): about 2.1 years of history gives about 3 usable folds, so no model or experiment can be called Eligible/Better. Backfill toward 5 years and/or shorten the 6-month hold-out (P6-00 Q1, Q2).",
      "Switch the nightly and weekly services on when you want plans (they start paused). The 10-session unattended exit of P7 starts counting from then. With the checklist inputs not yet wired (results calendar, F&O ban), every ticket needs a confirm-despite or lapses at the cutoff.",
      "Auto-approval (P7-12) is built but OFF and nothing calls it yet; turning it on is your authenticated act. Reserve promotion, depth recording and orders are decided: not built.",
      "Provide the Dhan credentials flow for the P3-01d probe (index and VIX history through the allow-listed endpoint); it unblocks P6-02 regime labels.",
      "Read the ADRs marked Proposed (0065, 0068–0081); every P6 slice was approved under the standing run instruction, not read by you.",
      "Define CPR, BB_SQUEEZE and FAILED_BREAKOUT if you want them: doc 07 does not define them well enough to build.",
    ],
  storyStatus: {
"P0-12": ["done", "2026-10-09", "Open questions answered (doc 01 §5)."],
"P0-13": ["done", "2026-10-11", "Dhan trial: consent flow and history proven."],
"P0-14": ["done", "2026-10-11", "Your 214 one-minute CSV files imported."],
"P0-15": ["done", "2026-10-09", "GPU benchmark done."],
"P0-16": ["done", "2026-10-09", "ML.NET spike run (LightGBM needs the container)."],
"P1-01": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-03": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-04": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-05": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-06": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-07": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-08": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-09": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-10": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-11": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-12": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-13": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-14": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-15": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-16": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-17": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-19": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-20": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-21": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P1-18": [
"review",
null,
"Exit check is yours: CSV history imported, 5 unattended EOD fetches, timeframes hand-checked."
],
"P2-01": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-02": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-03": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-04": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-05": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-06": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-07": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-08": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-09": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-10": [
"done",
"2026-10-15",
"Built per the roadmap (specs P1/P2, ADRs 0006\u20130030)."
],
"P2-11": [
"review",
null,
"Exit check is yours: values and XIRR vs spreadsheet for 3 accounts."
],
"P3-01": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-02": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-03": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-04": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-05": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-06": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-07": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-08": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-09": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-10": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-11": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-12": [
"done",
"2026-10-19",
"Built (P3 slices, ADRs 0031\u20130056)."
],
"P3-13": [
"review",
null,
"Assisted check built (P3-09c); your real 20-trade run is open."
],
"P4-01": [
"done",
"2026-10-21",
"Built (P4 slices, ADRs 0049\u20130054)."
],
"P4-02": [
"done",
"2026-10-21",
"Built (P4 slices, ADRs 0049\u20130054)."
],
"P4-03": [
"done",
"2026-10-21",
"Built (P4 slices, ADRs 0049\u20130054)."
],
"P4-05": [
"done",
"2026-10-21",
"Built (P4 slices, ADRs 0049\u20130054)."
],
"P4-06": [
"done",
"2026-10-21",
"Built (P4 slices, ADRs 0049\u20130054)."
],
"P4-07": [
"done",
"2026-10-21",
"Built (P4 slices, ADRs 0049\u20130054)."
],
"P4-04": [
"doing",
null,
"Sizing built (P3-03c); portfolio limits per account arrive with P7."
],
"P4-08": [
"review",
null,
"Chosen exits stable across folds: not run on real data yet."
],
"P5-01": [
"done",
"2026-10-22",
"No Hangfire: the lab_runs table is the queue (ADR 0058)."
],
"P5-02": [
"done",
"2026-10-22",
"ADR 0063."
],
"P5-03": [
"done",
"2026-10-22",
"ADR 0059."
],
"P5-04": [
"done",
"2026-10-22",
"ADR 0061 (a verb of the Api host)."
],
"P5-05": [
"done",
"2026-10-22",
"P5-08a/b."
],
"P5-06": [
"done",
"2026-10-22",
"Basket universe in P5-02; robustness ADR 0062."
],
"P5-07": [
"backlog",
null,
"Deferred (optional, riskiest slice)."
],
"P5-09": [
"ready",
null,
"Yours to run: 3 of your own ideas through the Lab."
],
"P6-01": [
"blocked",
null,
"Regime labels wait for the market-context job (P3-01d)."
],
"P6-02": [
"done",
"2026-10-23",
"ADR 0067"
],
"P6-03": [
"done",
"2026-10-23",
"ADR 0064 (regime-conditioned part waits on P6-01)"
],
"P6-04": [
"done",
"2026-10-23",
"ADR 0065"
],
"P6-05": [
"done",
"2026-10-23",
"ADR 0068 (FastTree here; LightGBM untested)"
],
"P6-06": [
"done",
"2026-10-23",
"ADR 0070, 0073"
],
"P6-07": [
"done",
"2026-10-23",
"ADR 0074 (proposals only; nothing pauses automatically)"
],
"P6-08": [
"done",
"2026-10-23",
"ADR 0076, 0073"
],
"P6-09": [
"done",
"2026-10-23",
"ADR 0078"
],
"P6-10": [
"done",
"2026-10-23",
"ADR 0072, 0077"
],
"P6-11": [
"done",
"2026-10-23",
"ADR 0075, 0069"
],
"P6-12": [
"doing",
null,
"12 strategies built (ADR 0079, 0081); CPR, BB_SQUEEZE, FAILED_BREAKOUT, RS_ROTATION deferred."
],
"P7-01": ["done", "2026-10-23", "Selection core, portfolio fill (ADR 0082, 0084)."],
"P7-02": ["done", "2026-10-23", "Per-account allocation, paper only (ADR 0089)."],
"P7-03": ["doing", null, "Checklist and review workflow built (ADR 0087, 0095); the review screen is P7-17."],
"P7-04": ["done", "2026-10-24", "Grading and auto-journal (ADR 0090, 0093)."],
"P7-05": ["doing", null, "Auto-approval built and OFF (ADR 0091); pre-open check and arming (P7-11) not built."],
"P7-06": ["done", "2026-10-23", "ADR 0085; market gate unavailable until the context job."],
"P7-07": ["done", "2026-10-24", "Weekly digest (ADR 0094); the service starts paused."],
"P7-08": ["doing", null, "Nightly pipeline wired and starts paused (ADR 0086, 0092); plan tickets joining the live set is P7-11."],
"P7-09": ["backlog", null, "Decided 2026-10-23: depth recording is not in P7."],
"P7-10": ["backlog", null, "Needs the nightly service switched on for 10 consecutive sessions."]
},
    phases: [
      { id: "P0", t: "Foundations", state: "built", note: "Repo, stack, prototype, design docs." },
      { id: "P1", t: "Data platform & instruments", state: "built", note: "Dhan adapter, CSV import, bars, aggregations, baskets, accounts. Exit gate not recorded in the roadmap: confirm." },
      { id: "P2", t: "Portfolio monitor & live feed", state: "built", note: "Holdings, trades, XIRR, charges, dividends, live feed. Exit gate not recorded in the roadmap: confirm." },
      {
        id: "P3", t: "Simulator, DSL & first strategies", state: "built", note: "Built through the stored strategies and the result store; the owner's real-run checks are open.",
        slices: [
          ["P3-01 to P3-01c", "Streaming indicators, frame API, weekly/monthly, context types", "built", "0031–0033"],
          ["P3-02 to P3-02b2", "Rules language, compiler, typed strategy model, exit tables", "built", "0034–0036"],
          ["P3-02c", "Strategy file: YAML, JSON Schema, content hash", "built", "0055"],
          ["P3-02d", "Stored strategies, lab tables, seed of the built-ins", "built", "0056"],
          ["P3-03 to P3-03f", "Simulator, scanner, fills, costs, swing positions", "built", "0037–0044"],
          ["P3-04 to P3-04c", "Run from stored bars, export library, simulate verb, trade cards", "built", "0041"],
          ["P3-05, P3-06", "VWAP_PULLBACK and BASE_BREAKOUT", "built", ""],
          ["P3-08, P3-09, P3-09c", "Independent walker check, verification pack, assisted check", "built", "0048"],
          ["P3-10", "Result store: sim_runs and sim_trades", "built", "0045"],
          ["P3-04d, P3-09d", "Conditions on cards; automated gate", "draft", ""],
          ["P3-01d", "Context data job (index, VIX): needs a probe of the allow-listed endpoint", "blocked", "0033"],
          ["P3-01e, P3-02e, P3-02f", "Breadth and sector RS; BUY to SELL mirror; optional", "later", ""],
        ],
      },
      {
        id: "P4", t: "Exits & exit learning", state: "built", note: "Exit tape, partials, trails, pyramid, same-entries comparison, per-cell exit choice.",
        slices: [
          ["P4-01", "Exit tape and trades with legs", "built", "0049"],
          ["P4-02", "Partial exits and the time stop", "built", ""],
          ["P4-03", "ATR, structure and EMA trails", "built", ""],
          ["P4-04", "The pyramid", "built", ""],
          ["P4-05", "Same-entries comparison and MAE/MFE diagnostics", "built", "0053"],
          ["P4-06", "Per-cell exit choice by walk-forward", "built", "0054"],
        ],
      },
      {
        id: "P5", t: "Strategy Lab v1", state: "built", note: "Exit criterion (3 of your own ideas tested end to end) is yours to run. xd-sandbox (P5-09) deferred.",
        slices: [
          ["P5-01", "Lab API: validate and the strategy library", "built", "0057"],
          ["P5-02", "Lab runs: record, queue, quick test, hub progress", "built", "0058"],
          ["P5-03", "Test ledger and the gates (multiple-testing factor)", "built", "0059"],
          ["P5-04", "Full test: walk-forward over timeframe, side, exit", "built", "0060"],
          ["P5-05", "Robustness: sweep, Monte Carlo, by year, sign-flip baseline", "built", "0062"],
          ["P5-06", "Verdict and report: JSON, inert HTML, zip", "built", "0063"],
          ["P5-07", "xd CLI (verb of the Api host behind scripts/xd.sh)", "built", "0061"],
          ["P5-08a, P5-08b", "Lab page: editor and validate; runs, verdict, report, ledger", "built", ""],
          ["P5-09", "xd-sandbox for C# plugins", "optional", ""],
        ],
      },
      {
        id: "P6", t: "Learning engine & ML.NET model", state: "building", note: "Everything that does not need regime labels is built. Models are advisory until P7; nothing is wired into selection.",
        slices: [
          ["P6-01", "Cell statistics: Bayesian shrinkage, recency", "built", "0064"],
          ["P6-03", "Triple-barrier labels", "built", "0065"],
          ["P6-04", "Decision-time features and the training set", "built", "0066"],
          ["P6-05", "Validation harness and baselines (purged folds, eligibility rule)", "built", "0067"],
          ["P6-06", "ML.NET trainer, PAV calibration, contributions (FastTree here; LightGBM untested)", "built", "0068"],
          ["P6-07", "Model registry and scoring; champion rule", "built", "0070"],
          ["P6-08", "Lifecycle and learning changelog", "built", "0073"],
          ["P6-09", "Drift monitors (replayed history; proposals only)", "built", "0074"],
          ["P6-10", "Hypothesis loop (one variable, owner approval)", "built", "0076"],
          ["P6-11", "Experiments: tweak and re-run with honest rules", "built", "0072"],
          ["P6-12", "Frozen-model counterfactual", "built", "0077"],
          ["P6-13", "Meta-labelling policy (take or skip)", "built", "0075"],
          ["P6-14", "Learned entry windows (proposals only)", "built", "0069"],
          ["P6-15", "Automatic loss tags", "built", "0071"],
          ["P6-16", "Lessons", "built", "0078"],
          ["P6-17a", "Intraday strategy batch: 8 of 9 as stored YAML (CPR deferred)", "built", "0079"],
          ["P6-17b", "Swing strategy batch: 4 of 6 (RS_ROTATION, BB_SQUEEZE, FAILED_BREAKOUT deferred)", "built", "0081"],
          ["P6-18", "Phase check and learning overview API", "built", "0080"],
          ["P6-02", "Regime labels and regime cells (needs P3-01d)", "blocked", ""],
        ],
      },
      {
        id: "P7", t: "Selection, accounts & nightly plan", state: "building", note: "Paper only, Advisory by default, no orders, no reserve promotion, no depth recording (your 2026-10-23 answers). Auto options are built and OFF.",
        slices: [
          ["P7-01", "Selection core: eligibility filters, merge, rank", "built", "0082"],
          ["P7-02", "Portfolio fill: primary, reserve, rejected", "built", "0084"],
          ["P7-03", "Plan record and state machines; approvals stamped by a DB trigger", "built", "0083"],
          ["P7-04", "Ticket builder (selection builder registered, off by default)", "built", "0088"],
          ["P7-05", "Pre-trade checklist per ticket and account", "built", "0087"],
          ["P7-06", "Smart rules in the plan (market gate unavailable until P3-01d/e)", "built", "0085"],
          ["P7-07", "Per-account allocation and sizing (paper accounts only)", "built", "0089"],
          ["P7-08", "Fail-closed nightly pipeline skeleton (starts paused)", "built", "0086"],
          ["P7-09", "Wire selection, rules, fill, allocation and checklist; suggestions inbox", "built", "0092"],
          ["P7-10", "Plan review and approval workflow, review-cutoff lapse", "built", "0095"],
          ["P7-11", "Pre-open check and arming (DB state and alerts only; reserve promotion OFF)", "draft", ""],
          ["P7-12", "Auto-approval at the cutoff: built, OFF, nothing calls it yet", "built", "0091"],
          ["P7-13", "Grading of primary, reserve, rejected and your decisions", "built", "0090"],
          ["P7-14", "Auto-journal from the read-only sync", "built", "0093"],
          ["P7-15", "Weekly learning digest (proposals only; weekly service paused)", "built", "0094"],
          ["P7-16", "Depth recording", "later", ""],
          ["P7-17", "Plan review screen (minimal) and Today tiles", "draft", ""],
          ["P7-18", "The phase check: ten consecutive unattended sessions", "draft", ""],
        ],
      },
      { id: "P8", t: "Reports & UI", state: "planned", note: "Learning screen and the reports; the P6 read APIs exist (cells, models, experiments, hypotheses, lessons, changelog, overview)." },
      { id: "P9", t: "AI assistant", state: "planned", note: "" },
      { id: "P10", t: "Paper trading", state: "planned", note: "Drift, lessons and the phase check need live records from here." },
      { id: "P11", t: "Small live", state: "planned", note: "" },
    ],
    honest: [
      "Built means: spec approved, code merged and deployed, scripts/check.sh green, an independent review done and its findings fixed. It does not mean it has run on your real data.",
      "Almost everything in P6 has run only on fabricated or synthetic data. The one real read (`xd learn phase-check`, 2026-10-23) found no model and no experiment stored: all three exit criteria NOT EVALUABLE.",
      "None of the 12 added strategies (8 intraday, 4 swing) has run on real data; none is claimed to have an edge.",
      "With about 2.1 years of data and a 6-month hold-out there are about 3 usable folds: the verdicts of P5/P6 can be Inconclusive at best until more history exists (ISS-19).",
    ],
  };
})();
