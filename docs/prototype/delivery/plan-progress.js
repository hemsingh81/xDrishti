/* xDrishti delivery plan — progress snapshot (what is built, where we are, what is next).
   Source of truth: docs/design/18-roadmap.md, docs/specs/, docs/adr/. Keep this file in sync when a slice lands:
   change a slice's state (built | approved | draft | later | blocked | optional), add its ADR, bump asOf.
   This view is the real state of the build; the Backlog/Board story ticks (plan-data-*.js, last synced 2026-10-09)
   are the original product-level plan and are not updated per slice. */
(function () {
  window.XDP = {
    asOf: "2026-10-26",
    now: "P0–P7 are built apart from regime labels (P6-02, waiting for index and VIX history) and depth recording (P7-16). P8 is built apart from the P8-16 browser matrix below (P8-18 to P8-21 are built, switches OFF; the P8-21 phone tunnel is your own act) and the P8-16 browser matrix, which still fails on C14 load time and some layout-shift cells; P8-17's check is built but reads MET only after your stopwatch. P10 paper trading is built end to end (P10-01 to P10-06) but nothing runs yet: the nightly and paper services are paused, the P7 phase check reads NOT MET and the paper phase check reads NOT EVALUABLE or NOT MET until real evidence, and a real MET also needs you to confirm the charge sheet (the writer, P8-23, is built). The agent now builds the Dhan allow-list slice (index and VIX history P3-01d/e, depth P7-16, read-only GET /v2/orders P11-01) and P9 (assistant: model and GPU chosen by the agent, the model download stays yours); every new setting defaults OFF and every UI write needs a fresh password. P11 to P15 wait for real answers and time: Dhan and ISP replies, 12+ weeks of paper evidence, recorded data. The long poles are wall-clock, not code. Nothing places an order.",
    owner: [
      "Switch the nightly, review-cutoff, pre-open, expiry, weekly, paper-capture and paper-review services on in Settings: this starts the clocks (P7's ten unattended sessions and P10's 12 weeks and 60 counted trades count only from then). Auto-approval stays OFF.",
      "Confirm the charge sheet in Settings (fresh password). Until you do, reports stay gross and neither the P8 nor the paper phase check can read MET (ISS-25).",
      "Do your P8-17 stopwatch run on real data (the P8 exit: what works, in which basket, on which timeframe, with which exit, in under 2 minutes).",
      "Ask Dhan and your ISP early (runbook P11-05): does the static-IP whitelist also apply to read-only data calls or only to orders; does a personal self-built algo under 10 orders a second need registration or an algo tag; sandbox behaviour; a static IP from your ISP (days of lead time, then a 7-day lock at Dhan).",
      "Read the ADRs marked Proposed (0065 to 0127; every slice was approved under the standing run instruction, not read by you). The SAFETY slices keep every new setting OFF until a deliberate act of yours.",
      "When P9 gets there, run the pinned model fetch script (the download stays yours).",
      "Later, only wall-clock and you can give: 8+ live weeks by hand, 30 semi-auto sessions, about 200 labelled announcements (P15), the 5-year backfill (ISS-19), a dedicated Dhan account for execution (P12-00), and your own 3 strategy ideas through the Lab with a trained model (`xd learn phase-check` reads NOT EVALUABLE until then).",
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
"P7-03": ["done", "2026-10-24", "Checklist, review workflow and the review screen built (ADR 0087, 0095; P7-17)."],
"P7-04": ["done", "2026-10-24", "Grading and auto-journal (ADR 0090, 0093)."],
"P7-05": ["done", "2026-10-24", "Auto-approval built and OFF (ADR 0091); pre-open check and arming built (ADR 0096); nothing is armed until you switch the services on."],
"P7-06": ["done", "2026-10-23", "ADR 0085; market gate unavailable until the context job."],
"P7-07": ["done", "2026-10-24", "Weekly digest (ADR 0094); the service starts paused."],
"P7-08": ["done", "2026-10-24", "Nightly pipeline wired and starts paused (ADR 0086, 0092); armed tickets join the live set (ADR 0096)."],
"P7-16": ["ready", null, "Built and deployed 2026-10-27, everything OFF. Before switching it on: check free disk (60 GiB), confirm your Dhan plan has 20-level depth, set XD_DEPTH_ENABLED=true, redeploy, send the password switch, resume the service. See ADR 0135."],
"P7-10": ["backlog", null, "Phase check built (`xd plan phase-check`, ADR 0097) and reads NOT MET: needs the services switched on for 10 consecutive scheduled sessions (Q20)."],
"P7-19": ["done", "2026-10-26", "Numeric ticket levels (ADR 0109)."],
"P7-20": ["done", "2026-10-26", "Crossing alerts and post-trade prompts (ADR 0115); alerts are OFF by default."],
"P8-01": ["done", "2026-10-24", "ADR 0098."],
"P8-02": ["done", "2026-10-24", "Read-only Learning screen: frame, Overview, Setups."],
"P8-03": ["done", "2026-10-25", "Lessons, Experiments, Models and Digest screens, read only."],
"P8-04": ["done", "2026-10-25", "Plan versus actual, Decisions, Autonomy (read), phase checks (ADR 0103)."],
"P8-05": ["done", "2026-10-24", "ADR 0099 (pure core, no endpoint)."],
"P8-06": ["done", "2026-10-24", "ADR 0100. Reports print net only after your stored charge-sheet confirmation (the writer, P8-23, is built; the confirmation is yours)."],
"P8-07": ["done", "2026-10-25", "Basket benchmarks (ADR 0102)."],
"P8-08": ["done", "2026-10-25", "Reports screen: Overview and What works (ADR 0104)."],
"P8-09": ["done", "2026-10-25", "Basket comparison, pivot builder and lenses (ADR 0105)."],
"P8-10": ["done", "2026-10-26", "Both halves built (ADR 0106, 0114)."],
"P8-10b": ["done", "2026-10-26", "Stored walk-forward exit choices read (ADR 0114)."],
"P8-11": ["done", "2026-10-26", "Drill-down and trade replay on Lightweight Charts (ADR 0125)."],
"P8-12": ["done", "2026-10-26", "Frozen-model counterfactual stored and shown (ADR 0110)."],
"P8-13": ["done", "2026-10-26", "Smart-rule, loss-tag and entry-window evidence, both halves (ADR 0111, 0112)."],
"P8-14": ["done", "2026-10-26", "Plan flags, banners, per-account standings, ticket chart (ADR 0113)."],
"P8-15": ["done", "2026-10-24", "ADR 0101; ticket alert counts carried by P8-15b (2026-10-25)."],
"P8-16": ["blocked", null, "Partial: global rules and the browser runner are built (ADR 0124, 2026-10-26); C14 load time and some layout-shift cells still fail, plus the Settings tab strip and replay findings."],
"P8-17": ["done", "2026-10-26", "Check built (ADR 0126); it reads MET only after your stopwatch (and P8-16 findings cleared); P8-17f cut Reports requests 21 to 11."],
"P8-18": ["done", "2026-10-26", "Capability gate (ADR 0128) and replay job (ADR 0129) built; both switches are OFF until you switch experiments_ui on at /owner-acts. SAFETY: fresh password per start; a result is a proposal, nothing is applied."],
"P8-19": ["done", "2026-10-26", "Owner-act forms built (ADR 0130): lesson and hypothesis moves, changelog revert, autonomy form. Both switches (learning_moves_ui, autonomy_form) are OFF until you switch them on at /owner-acts. SAFETY: fresh password per write, nothing applied, no order."],
"P8-20": ["done", "2026-10-27", "Exports built (ADR 0131): five Reports tables as CSV with the evidence block inside the file, nothing about accounts, one audit row each. The switch (exports) is OFF until you switch it on at /owner-acts. SAFETY: CSV only, caps refuse and never cut, no password per export (your approved decision D5). The Lab's report page and zip are now under the same switch (403 until you switch exports on)."],
"P8-21": ["done", "2026-10-27", "Installable app shell built (ADR 0132): a manifest, icons and an offline page; the worker caches six static files and never any data, and is removed on sign-out. The switch (pwa) is OFF until you switch it on at /owner-acts. The phone path (private tunnel) is your own security act and is not built or enabled; see the runbook in doc 17."],
"P8-22": ["done", "2026-10-26", "Audit log screen (ADR 0117)."],
"P9-01": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-02": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-03": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-04": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-05": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-06": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-07": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-08": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-09": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P9-10": ["ready", null, "Delegated to the agent on 2026-10-26: model and GPU chosen by the agent; the model file download stays yours (a pinned fetch script). Off by default."],
"P10-01": ["done", "2026-10-26", "Paper fill capture (ADR 0116)."],
"P10-02": ["done", "2026-10-26", "Paper ledger (ADR 0118)."],
"P10-03": ["done", "2026-10-26", "Weekly review and discipline report (ADR 0121)."],
"P10-04": ["done", "2026-10-26", "Backtest versus paper (ADR 0122)."],
"P10-05": ["done", "2026-10-26", "Config change governance (ADR 0119)."],
"P10-06": ["done", "2026-10-26", "Paper phase check (reads NOT EVALUABLE or NOT MET until real evidence; MET also needs your charge-sheet confirmation) (ADR 0123)."],
"P11-01": ["ready", null, "Delegated to the agent on 2026-10-26 with the Dhan allow-list slice (read-only GET /v2/orders); stays OFF until you allow it."],
"P11-05": ["ready", null, "Start first: the ISP lead time and Dhan's reply time are outside your control."],
"P11-O1": ["ready", null, "Send the Dhan and ISP questions now (runbook sections 3 and 4)."]
},
    phases: [
      { id: "P0", t: "Foundations", state: "built", note: "Repo, stack, prototype, design docs." },
      { id: "P1", t: "Data platform & instruments", state: "built", note: "Dhan adapter, CSV import, bars, aggregations, baskets, accounts. Exit gate not recorded in the roadmap: confirm." },
      { id: "P2", t: "Portfolio monitor & live feed", state: "built", note: "Holdings, trades, XIRR, charges, dividends, live feed. Exit gate not recorded in the roadmap: confirm." },
      {
        id: "P3", t: "Simulator, DSL & first strategies", state: "built", note: "Built through the stored strategies, the result store, the mirror, lag-on-calls and decision traces; your real-run checks are open. Index and VIX history and breadth (P3-01d/e) are delegated to the agent with the Dhan allow-list slice.",
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
          ["P3-04d", "Decision trace and the conditions at the decision bar on trade cards (built 2026-10-25)", "built", "0041", {"d": 2, "s": "P3-04d-decision-trace-and-conditions-on-cards"}],
          ["P3-09d", "Automated gate (verify-pack --gate): the P3 exit (built 2026-10-21; the real run on your pack is open)", "built", "0048"],
          ["P3-01d0", "Probe: can index and VIX 1-minute history be had through the allow-listed intraday endpoint, and how far back (delegated to the agent with the Dhan allow-list slice)", "approved", "", {"d": 0.5, "s": "P3-01d-context-data-job-index-and-vix"}],
          ["P3-01d", "Context data job: NIFTY 50 and INDIA VIX into market.context_daily (built 2026-10-27; to use it you connect Dhan, track the two indices and run the probe)", "built", "0033", {"d": 3.5, "s": "P3-01d-context-data-job-index-and-vix"}],
          ["P3-01d2", "Daily Dhan client for index and VIX (only if the probe fails; a daily endpoint is your security decision)", "later", "", {"d": 1.5, "o": 1, "s": "P3-01d-context-data-job-index-and-vix"}],
          ["P3-01d3", "Daily CSV loader for index and VIX (only if the probe fails)", "later", "", {"d": 1.5, "s": "P3-01d-context-data-job-index-and-vix"}],
          ["P3-01e", "Breadth of your tracked shares, point in time, and the market gate input (built 2026-10-27; needs tracked shares and the two indices)", "built", "0134", {"d": 2.5, "s": "P3-01e-breadth-point-in-time"}],
          ["P3-02e", "BUY to SELL mirror: a closed rewrite table on the syntax tree (built 2026-10-25)", "built", "0107", {"d": 3, "s": "P3-02e-buy-to-sell-mirror"}],
          ["P3-02f", "Lag on a feature call and an always-false lint (optional; supersedes an approved restriction, needs your decision and an ADR) (built 2026-10-25)", "built", "0108", {"d": 2, "s": "P3-02f-lag-on-feature-calls-and-always-false-lint"}],
          ["P3-lift", "The simulator reads the market context (built 2026-10-27; point-in-time; needs_context lifted; is_expiry_day stays refused with its reason)", "built", "0145", {"d": 2, "s": "P3-lift-simulator-reads-the-market-context"}],
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
          ["P6-02", "Regime labels per day, regime tags on trades and regime cells (built 2026-10-28; basis never mixed; Reports dimension, training feature and loss tag follow as P6-02b)", "built", "0146", {"d": 2, "s": "P6-02-regime-labels-and-regime-cells"}],
        ],
      },
      {
        id: "P7", t: "Selection, accounts & nightly plan", state: "building", note: "Paper only, Advisory by default, no orders, no reserve promotion (your 2026-10-23 answers). Auto options are built and OFF. Numeric ticket levels (P7-19) and crossing alerts (P7-20, OFF by default) are built. Depth recording (P7-16) is delegated to the agent with the Dhan allow-list slice and stays OFF until you switch it on. The ten-session exit needs the services switched on.",
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
          ["P7-11", "Pre-open check and arming (DB state and alerts only; reserve promotion OFF)", "built", "0096"],
          ["P7-12", "Auto-approval at the cutoff: built, OFF, nothing calls it yet", "built", "0091"],
          ["P7-13", "Grading of primary, reserve, rejected and your decisions", "built", "0090"],
          ["P7-14", "Auto-journal from the read-only sync", "built", "0093"],
          ["P7-15", "Weekly learning digest (proposals only; weekly service paused)", "built", "0094"],
          ["P7-16", "Depth recording service (Data account, 20-level depth; new allow-list entry, token on a second connection, disk) (built 2026-10-27; three gates, all OFF: Depth__Enabled, your password switch, the paused service)", "built", "0135", {"d": 4, "o": 1, "s": "P7-16-depth-recording-service"}],
          ["P7-16b", "Trade-tick recording (aggressive-volume source for P13)", "later", "", {"d": 2, "o": 1}],
          ["P7-17", "Plan review screen (minimal) and Today tiles", "built", ""],
          ["P7-18", "The phase check: ten consecutive unattended sessions (reads NOT MET until the services have run)", "built", "0097"],
          ["P7-19", "Numeric ticket levels: entry reference, stop and target stored beside the text (built 2026-10-26)", "built", "0109", {"d": 2, "s": "P7-19-numeric-ticket-levels"}],
          ["P7-20", "Price-crossing alerts on armed tickets and post-trade prompts (built 2026-10-26; alerts are OFF by default)", "built", "0115", {"d": 3, "o": 1, "s": "P7-20-price-crossing-alerts-and-post-trade-prompts"}],
        ],
      },
      {
        id: "P8", t: "Reports & UI", state: "building", note: "Built: every read-only screen and report, the drill-down and trade replay, plan follow-ups, the audit log, the P8 phase check and the charge-sheet confirmation writer. P8-16 (responsive, accessibility, performance) is partial: the browser runner is built but C14 load time and some layout-shift cells still fail. P8-18 to P8-21 are built (switches OFF); they are not on the exit path.",
        slices: [
          ["P8-01", "Shared analytics kit and bundle guard", "built", "0098", {"d": 3, "s": "P8-01-analytics-kit-and-bundle-guard"}],
          ["P8-02", "Learning screen: frame, Overview and Setups (built 2026-10-24)", "built", "", {"d": 4, "s": "P8-02-learning-screen"}],
          ["P8-02b", "HeatGrid in the shared kit and a lazy tab registry (built 2026-10-25)", "built", "", {"d": 0.5}],
          ["P8-03", "Lessons, Experiments, Hypotheses, Models and Digest screens (read only) (built 2026-10-25)", "built", "", {"d": 4.5, "s": "P8-03-lessons-experiments-hypotheses-and-models-screens"}],
          ["P8-04", "Plan versus actual, Decisions, Autonomy (read) and the phase checks (built 2026-10-25)", "built", "0103", {"d": 4.5, "s": "P8-04-plan-versus-actual-decisions-autonomy-and-phase-checks"}],
          ["P8-05", "Report engine core (pure)", "built", "0099", {"d": 4, "s": "P8-05-report-engine-core"}],
          ["P8-06", "Report read endpoints over stored runs", "built", "0100", {"d": 4.5, "s": "P8-06-report-read-endpoints"}],
          ["P8-07", "Basket benchmarks (buy-and-hold and the benchmark line) (built 2026-10-25)", "built", "0102", {"d": 3, "s": "P8-07-basket-benchmarks"}],
          ["P8-08", "Reports screen: frame, Overview, auto-insights and What works (built 2026-10-25)", "built", "0104", {"d": 4, "s": "P8-08-reports-screen-overview-and-what-works"}],
          ["P8-09", "Basket comparison, pivot builder and lenses (built 2026-10-25)", "built", "0105", {"d": 3.5, "s": "P8-09-basket-comparison-pivot-builder-and-lenses"}],
          ["P8-10", "Catalogue reports the stored data can serve (strategies, side, calendar, MAE/MFE, instruments, sector, exit comparison, Monte Carlo) (both halves built, 2026-10-25 and 2026-10-26)", "built", "0106, 0114", {"d": 5, "s": "P8-10-catalogue-reports-the-stored-data-can-serve"}],
          ["P8-10b", "Read of research.exit_selections (the strongest 'which exit' evidence) (built 2026-10-26)", "built", "0114", {"d": 2}],
          ["P8-11", "Drill-down, trade replay and the shared price chart (built 2026-10-26)", "built", "0125", {"d": 4, "s": "P8-11-drill-down-and-trade-replay-shared-price-chart"}],
          ["P8-12", "Journey: the frozen-model counterfactual stored and shown (built 2026-10-26)", "built", "0110", {"d": 4, "s": "P8-12-journey-counterfactual-stored-and-shown"}],
          ["P8-13", "Smart-rules, loss-tags and entry-window evidence (both halves built 2026-10-26)", "built", "0111, 0112", {"d": 3.5, "s": "P8-13-smart-rules-loss-tags-and-entry-window-evidence"}],
          ["P8-14", "Plan follow-ups: structured flags, banners, per-account standings, ticket charts (built 2026-10-26)", "built", "0113", {"d": 5, "s": "P8-14-plan-follow-ups-flags-banners-and-ticket-charts"}],
          ["P8-15", "Plan notifications in the bell (read only, in-app)", "built", "0101", {"d": 3, "s": "P8-15-plan-notifications"}],
          ["P8-15b", "Ticket alert and prompt counts carried in the plan notices (built 2026-10-25)", "built", "0115", {"d": 1}],
          ["P8-16", "Responsive, accessibility and performance pass over every P8 screen (partial: global rules and browser runner built 2026-10-26; C14 load time and some layout-shift cells still fail)", "blocked", "0124", {"d": 3.5, "s": "P8-16-responsive-accessibility-and-performance-pass"}],
          ["P8-17", "The P8 phase check: interaction budget and your stopwatch (built 2026-10-26; MET needs your stopwatch)", "built", "0126", {"d": 2, "s": "P8-17-the-phase-check"}],
          ["P8-17f", "Reports page fan-out 21 to 11 requests (built 2026-10-26)", "built", "0126", {"d": 1}],
          ["P8-18", "Tweak and re-run from the UI (starts one replay job, never applies) (built 2026-10-26; SAFETY: setting OFF, fresh password per UI write)", "built", "0128, 0129", {"d": 4.5, "o": 1, "s": "P8-18-tweak-and-re-run-from-the-ui"}],
          ["P8-19", "Owner-act forms: autonomy, changelog revert, lesson and hypothesis moves (built 2026-10-26; SAFETY: setting OFF, fresh password per UI write)", "built", "0130", {"d": 4, "o": 1, "s": "P8-19-owner-act-forms-autonomy-revert-lesson-and-hypothesis-moves"}],
          ["P8-20", "Exports (research tables as CSV with the evidence block) (built 2026-10-27; SAFETY: switch OFF, CSV only, audited)", "built", "0131", {"d": 3, "o": 1, "s": "P8-20-exports"}],
          ["P8-21", "PWA installable shell (assets only, no offline data) (built 2026-10-27; SAFETY: switch OFF; tunnel not built)", "built", "0132", {"d": 3, "o": 1, "s": "P8-21-pwa-installable-shell"}],
          ["P8-22", "Audit log screen (optional, read only) (built 2026-10-26)", "built", "0117", {"d": 3, "s": "P8-22-audit-log-screen"}],
          ["P8-23", "Charge-sheet confirmation writer: owner act, fresh password, off by default (built 2026-10-26; the confirmation itself is yours)", "built", "0127", {"d": 3, "o": 1, "s": "P8-23-charge-sheet-confirmation-writer"}],
        ],
      },
      {
        id: "P9", t: "AI assistant", state: "planned", note: "Local assistant: off by default, every number checked against tool results, never decides a trade. Delegated to the agent (2026-10-26): model and GPU chosen by the agent, the model download stays yours (a pinned fetch script). P9-01 to P9-03, P9-05, P9-07, P9-08 and P9-10 are SAFETY.",
        slices: [
          ["P9-01", "xd-llm service, model runtime and LLM client (built 2026-10-27, OFF: model Qwen3-30B-A3B in a container, pinned in ADR 0136; start with scripts/xd-ai.sh, the real-model measurements are still open)", "built", "0136", {"d": 2, "o": 1, "s": "P9-01-xd-llm-service-and-model-runtime"}],
          ["P9-02", "xd-mcp: read-only tool server, two read-only database roles and the aiv views (built and security-reviewed 2026-10-27, OFF: two read-only database roles, 35 read-only views, a token-gated tool server; start with scripts/xd-ai.sh start --tools-only)", "built", "0137", {"d": 3, "o": 1, "s": "P9-02-xd-mcp-read-only-tool-server"}],
          ["P9-03", "Assistant endpoint: streaming chat, tool loop, grounding check and ai audit tables (built 2026-10-27, OFF: switching it on is your act with a fresh password; no real model was run)", "built", "0138", {"d": 3, "o": 1, "s": "P9-03-assistant-endpoint-streaming-and-ai-audit-tables"}],
          ["P9-04", "Assistant chat screen (built and reviewed 2026-10-27; the assistant stays OFF until you switch it on; Assistant is in the Research menu)", "built", "0138", {"d": 3, "s": "P9-04-assistant-chat-ui"}],
          ["P9-05", "'Ask your data' tools (built 2026-10-27, OFF: eleven read-only tools and a chart tool over the report engine and the reviewed views; the portfolio tool stays empty until you switch the portfolio context on; no real model was run)", "built", "0139", {"d": 3, "o": 1, "s": "P9-05-ask-your-data-tools"}],
          ["P9-06", "Daily briefing and weekly review text (built 2026-10-27, OFF: the model phrases, a checker rejects any figure not in the stored facts and the fixed-format text is stored instead; both services start paused and the model switch is off; no real model was run)", "built", "0140", {"d": 3, "s": "P9-06-daily-briefing-and-weekly-review-text"}],
          ["P9-07", "Strategy drafting: English idea to validated YAML, your confirm, then Lab run (built 2026-10-27, OFF: the model writes text, the Lab validator and checks decide, only your confirm with the switch and a fresh password stores a version; the chat tool, the auto quick test (off; your act with a password) and the CLI verb are built; no real model was run)", "built", "0141", {"d": 3.5, "o": 1, "s": "P9-07-strategy-drafting-via-lab-validate-and-run"}],
          ["P9-08", "Hypothesis and suggestion proposals (built 2026-10-27, OFF: the model proposes, the engine re-reads the evidence and checks every rule, you accept with the switch and a fresh password; hypothesis and basket add/remove; the auto option is hypotheses only; next-day focus not built; no real model was run)", "built", "0142", {"d": 3.5, "o": 1, "s": "P9-08-hypothesis-and-suggestion-proposals"}],
          ["P9-09", "Eval set and regression runner (30 data questions, 10 plans, 10 ideas) (built 2026-10-27: 30 data questions, plan and idea sets, graders, runner; runs only on your command once the model is up)", "built", "0143", {"d": 3.5, "s": "P9-09-eval-set-and-regression-runner"}],
          ["P9-10", "Hermes PoC: hardened network-isolated container and the adopt/reject decision (built 2026-10-27: the hardened container definition, scripts and checklist only; nothing third-party was fetched or started; the adopt/reject decision is yours)", "built", "0144", {"d": 3, "o": 1, "s": "P9-10-hermes-poc-hardened-container"}],
        ],
      },
      {
        id: "P10", t: "Paper trading", state: "building", note: "P10-01 to P10-06 are built and deployed; the services are paused. Paper only, places no order. The clock is wall-clock: 12 weeks and 60 counted trades, and it starts only when you switch the P7 services on. Weekly routine about 85 minutes.",
        slices: [
          ["P10-01", "Paper fill capture: an armed ticket becomes a timed, priced paper trade (built 2026-10-26; service paused until you switch it on)", "built", "0116", {"d": 3, "o": 1, "s": "P10-01-paper-fill-capture"}],
          ["P10-02", "Paper ledger and open positions (equity, open risk, what the plan reads) (built 2026-10-26; the plan reads the paper book only when its setting is on)", "built", "0118", {"d": 3, "o": 1, "s": "P10-02-paper-ledger-and-open-positions"}],
          ["P10-03", "Weekly review and the discipline report (deviations from the plan) (built 2026-10-26; service paused until you switch it on)", "built", "0121", {"d": 3, "s": "P10-03-weekly-review-and-discipline-report"}],
          ["P10-04", "Backtest versus paper: the band and the drawdown reference at the paper's own n (built 2026-10-26)", "built", "0122", {"d": 2.5, "s": "P10-04-backtest-vs-paper-report"}],
          ["P10-05", "Config change governance: fingerprint, measurement epochs, frozen/risk-down/free (built 2026-10-26)", "built", "0119", {"d": 3, "o": 1, "s": "P10-05-config-change-governance-via-the-hypothesis-loop"}],
          ["P10-06", "The paper phase check (MET, NOT MET, NOT EVALUABLE) (built 2026-10-26; reads NOT EVALUABLE or NOT MET until real evidence, MET also needs your charge-sheet confirmation)", "built", "0123", {"d": 2, "s": "P10-06-paper-phase-check"}],
          ["P10-07", "Per-cell paper facts for the Paper to Active gate (P6-08)", "later", "", {"d": 2}],
        ],
      },
      {
        id: "P11", t: "Small live", state: "planned", note: "Manual live at 25-50% of intended risk; no order code. The static-IP and SEBI enquiry (P11-05) starts first because of ISP lead time. Exit needs at least 8 weeks of live sessions.",
        slices: [
          ["P11-05", "Static IP and SEBI re-check: runbook, questions and the compliance record", "draft", "", {"d": 2, "o": 1, "s": "P11-05-static-ip-and-sebi-recheck-runbook"}],
          ["P11-04", "Scale-up rules and risk ramp: live-ready gate, steps S1/S2/S3, evidence gates, halts", "draft", "", {"d": 5, "o": 1, "s": "P11-04-scale-up-rules-and-risk-ramp"}],
          ["P11-01", "Manual-live fill capture: trade book and order book read from Dhan (waits for P11-04A live allocations; the read-only GET entries are accepted in principle, ADR 0133)", "approved", "", {"d": 3.5, "o": 1, "s": "P11-01-manual-live-fill-capture"}],
          ["P11-02", "Execution feedback: grade on fills, slippage per liquidity bucket, capacity score", "draft", "", {"d": 3.5, "s": "P11-02-execution-feedback-grade-on-fills-slippage-and-capacity"}],
          ["P11-03", "Live versus paper report: paired tickets, slippage distribution, execution gap", "draft", "", {"d": 3, "s": "P11-03-live-vs-paper-slippage-report"}],
          ["P11-06", "The small-live phase check (eight weeks, eight criteria)", "draft", "", {"d": 2, "s": "P11-06-small-live-phase-check"}],
        ],
      },
      {
        id: "P12", t: "Execution service (xd-exec)", state: "planned", note: "The first order-capable code, in its own project and container; nothing sends an order until you promote an account (password). Exit is wall-clock: 10 assisted then 30 semi-auto sessions, zero reconciliation breaks.",
        slices: [
          ["P12-01", "xd-exec skeleton and the order state machine (the only order-capable project)", "draft", "", {"d": 3, "o": 1, "s": "P12-01-xd-exec-skeleton-and-order-state-machine"}],
          ["P12-02", "Dhan order adapter, send protocol, FakeBroker and sandbox", "draft", "", {"d": 3, "o": 1, "s": "P12-02-dhan-order-adapter-and-sandbox"}],
          ["P12-03", "Pre-trade checks (33 checks; unavailable is a reject)", "draft", "", {"d": 2.5, "o": 1, "s": "P12-03-pre-trade-checks"}],
          ["P12-04", "Broker-side stops and the protection invariant", "draft", "", {"d": 3, "o": 1, "s": "P12-04-broker-side-stops-and-protection"}],
          ["P12-05", "Kill switch and halts", "draft", "", {"d": 2, "o": 1, "s": "P12-05-kill-switch-and-halts"}],
          ["P12-06", "Order update stream, crash recovery and reconciliation", "draft", "", {"d": 3, "o": 1, "s": "P12-06-order-update-stream-and-reconciliation"}],
          ["P12-07", "Account execution modes: paper, manual live, assisted (password, evidence gates)", "draft", "", {"d": 3.5, "o": 1, "s": "P12-07-account-execution-modes-paper-manual-assisted"}],
          ["P12-08", "Semi-auto: the one-tap confirm ticket (exact text, time-boxed, idempotent)", "draft", "", {"d": 4, "o": 1, "s": "P12-08-semi-auto-one-tap-confirm"}],
          ["P12-09", "auto_live within hard limits (caps, guard, always-ask list)", "draft", "", {"d": 3, "o": 1, "s": "P12-09-auto-live-within-hard-limits"}],
          ["P12-10", "Execution screens and notifications", "draft", "", {"d": 3, "o": 1, "s": "P12-10-execution-screens-and-notifications"}],
          ["P12-11", "Daily login, token and static-IP operations: readiness and the 09:10 gate", "draft", "", {"d": 2.5, "o": 1, "s": "P12-11-daily-login-token-and-static-ip-operations"}],
          ["P12-B1", "Ticket-state bridge: write Triggered and Closed on plan tickets from execution", "later", "", {"d": 1.5}],
          ["P12-12", "The phase check: 30 semi-auto sessions with zero reconciliation breaks", "draft", "", {"d": 2, "s": "P12-12-phase-check-30-semi-auto-sessions"}],
          ["P12-13", "Swing, partial-exit and pyramid execution (out of P12, UNSIZED)", "later", "", {}],
          ["P12-14", "Order-group concept for two-leg pairs (UNSIZED, before any live PAIRS)", "later", "", {}],
        ],
      },
      {
        id: "P13", t: "Tick & depth features", state: "planned", note: "Plan only (slices P13-01 to P13-10, about 23 agent-days). Waits for at least 6 months of recorded depth (P7-16, your decision).",
        slices: [
          ["P13-01", "Recording coverage and capacity report", "draft", "", {"d": 2, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-02", "Book reader and as-of reconstruction", "draft", "", {"d": 2.5, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-03", "Feature catalog md-1 and its extraction (point in time)", "draft", "", {"d": 3, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-04", "Depth slippage estimator", "draft", "", {"d": 2.5, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-05", "Feature-set comparison core (shared with P15-10)", "draft", "", {"d": 3, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-06", "Evaluation run and verdict record", "draft", "", {"d": 2, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-07", "Shadow challenger and your keep/drop", "draft", "", {"d": 2, "o": 1, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-08", "Depth-aware pre-trade check (cut line)", "draft", "", {"d": 3, "o": 1, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-09", "Microstructure report in the Reports and Learning screens", "draft", "", {"d": 2, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-10", "P13 phase check: months recorded, folds, n, verdict", "draft", "", {"d": 1, "s": "P13-00-tick-and-depth-features-plan"}],
        ],
      },
      {
        id: "P14", t: "Options & statistical arbitrage", state: "planned", note: "Plan only (slices P14-01 to P14-17, about 44 agent-days). Research only: no order, no margin call; stays disabled unless it pays after costs.",
        slices: [
          ["P14-01", "Option contracts, lot-size history, expiry and rule epochs", "draft", "", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-02", "Chain snapshot recorder (indices every 5 minutes, stock chains at end of day)", "draft", "", {"d": 3, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-03", "Expired-options backfill", "draft", "", {"d": 3, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-04", "Archive quality checks", "draft", "", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-05", "Option pricing core (pure): Black-Scholes, implied-volatility solver, Greeks", "draft", "", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-06", "Instrument-expression dimension (cash, futures, option)", "draft", "", {"d": 2.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-07", "Option cost and liquidity model", "draft", "", {"d": 2.5, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-08", "Option expression simulator", "draft", "", {"d": 3.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-09", "Expression evaluation and verdict", "draft", "", {"d": 3, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-10", "Defined-risk structures: debit spread, credit spread, iron condor", "draft", "", {"d": 3.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-11", "IV and event features", "draft", "", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-12", "Pairs core (pure): spread, hedge ratio, cointegration test", "draft", "", {"d": 3, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-13", "Pairs simulation and evaluation (intraday cash pairs)", "draft", "", {"d": 3.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-14", "Basis data and carry model", "draft", "", {"d": 2.5, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-15", "Basis evaluation (first to be cut)", "draft", "", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-16", "Reports: coverage, expression comparison, pairs", "draft", "", {"d": 3, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-17", "P14 phase check", "draft", "", {"d": 1.5, "s": "P14-00-options-and-stat-arb-plan"}],
        ],
      },
      {
        id: "P15", t: "Fundamentals & events", state: "planned", note: "Plan only (slices P15-01 to P15-12, about 30 agent-days). Needs the 5-year backfill (ISS-19) and your 200 labelled announcements.",
        slices: [
          ["P15-01", "Filing sources and the terms gate (folder drop first; public fetcher OFF)", "draft", "", {"d": 3, "o": 1, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-02", "Point-in-time filing store", "draft", "", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-03", "Results XBRL parser (pure)", "draft", "", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-04", "Calendar and shareholding", "draft", "", {"d": 2, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-05", "Features ev-1 and fu-1", "draft", "", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-06", "Event-risk filter and loss tag", "draft", "", {"d": 2, "o": 1, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-07", "Announcement eval set and labelling (you label about 200 announcements)", "draft", "", {"d": 2, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-08", "LLM announcement flags and field extraction (schema-checked, never decides trades)", "draft", "", {"d": 3, "o": 1, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-09", "PEAD strategy and the longer swing hold (max_hold up to 20)", "draft", "", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-10", "Evaluation and verdicts", "draft", "", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-11", "Screens", "draft", "", {"d": 2, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-12", "P15 phase check", "draft", "", {"d": 1, "s": "P15-00-fundamentals-and-events-plan"}],
        ],
      },
    ],
    honest: [
      "Specs written on 2026-10-24 (P9, P11 to P15 and the rest of the leftovers) are Drafts under your delegation: each design question is answered inside its spec with a reason, but sizes are uncalibrated (earlier phases overran) and facts about Dhan, SEBI, NSE and library versions were read on 2026-10-24 (primary SEBI and NSE texts could not be opened and carry a re-check step). The P8 and P10 slices built since have run on test fixtures; only your stopwatch and real paper sessions will tell whether they work on your data.",
      "Backlog and Board points are your own hours (1 pt = 1 focused hour: reading the spec, approving, checking the result, your acts); agent build effort is in agent-days, in each story's note and each slice here.",
      "Built means: spec approved, code merged and deployed, scripts/check.sh green, an independent review done and its findings fixed. It does not mean it has run on your real data.",
      "Almost everything in P6 has run only on fabricated or synthetic data. The one real read (`xd learn phase-check`, 2026-10-23) found no model and no experiment stored: all three exit criteria NOT EVALUABLE.",
      "None of the 12 added strategies (8 intraday, 4 swing) has run on real data; none is claimed to have an edge.",
      "With about 2.1 years of data and a 6-month hold-out there are about 3 usable folds: the verdicts of P5/P6 can be Inconclusive at best until more history exists (ISS-19).",
    ],
  };
})();
