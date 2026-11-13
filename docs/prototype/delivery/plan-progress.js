/* xDrishti delivery plan — progress snapshot (what is built, where we are, what is next).
   Source of truth: docs/design/18-roadmap.md, docs/specs/, docs/adr/. Keep this file in sync when a slice lands:
   change a slice's state (built | approved | draft | later | blocked | optional), add its ADR, bump asOf.
   This view is the real state of the build; the Backlog/Board story ticks (plan-data-*.js, last synced 2026-10-09)
   are the original product-level plan and are not updated per slice. */
(function () {
  window.XDP = {
    asOf: "2026-11-11",
    now: "P0–P11, P15 and the new AU programme (automation and simplicity, 19 slices, 2026-11-09/10) are built, every new switch OFF. CF (confidence programme) is built except CF-04 Part B (Dhan 1-minute backfill). P14 is built except the basis pair (P14-14/15). BK (system baskets) is under way: BK-02 built, BK-01 in build. The one open P8 item is still the P8-16 browser matrix (C14 load time and 12 layout-shift cells fail after the 2026-11-09 first-load cut); P8-17 reads MET only after your stopwatch. P10 has nothing running yet: the nightly and paper services are paused, and the P7, paper and AU checks read NOT MET or NOT EVALUABLE until real days, real evidence and your charge-sheet confirmation. P12 (xd-exec) is built in reduced form: semi_auto and auto_live are not values and nothing places a live order. P13 is untouched. Long poles are wall-clock, not code: Dhan and ISP replies, 12+ weeks of paper evidence, 30 semi-auto sessions, recorded data. Nothing places an order.",
    owner: [
      "Switch the nightly, review-cutoff, pre-open, expiry, weekly, paper-capture and paper-review services on in Settings: this starts the clocks (P7's ten unattended sessions and P10's 12 weeks and 60 counted trades count only from then). Auto-approval stays OFF.",
      "Confirm the charge sheet in Settings (fresh password). Until you do, reports stay gross and neither the P8 nor the paper phase check can read MET (ISS-25).",
      "Do your P8-17 stopwatch run on real data (the P8 exit: what works, in which basket, on which timeframe, with which exit, in under 2 minutes).",
      "Ask Dhan and your ISP early (runbook P11-05): does the static-IP whitelist also apply to read-only data calls or only to orders; does a personal self-built algo under 10 orders a second need registration or an algo tag; sandbox behaviour; a static IP from your ISP (days of lead time, then a 7-day lock at Dhan).",
      "Decide on the Auto switch (AU-17, Settings, password): OFF by default; ON resumes the chain services and gives next-day suggestions only, never orders, and it refuses to turn on while the charge sheet is unconfirmed. Learned settings changes only tighten; loosening or undoing one needs your password (AU-10).",
      "CF switches are report-only or OFF: Gate modes, automatic model rollback and automatic kill-rule demotion stay OFF until you turn them on; record the verify-pack gate result per strategy version (readiness row 15). CF-04 Part A's NSE fetch stays OFF until you fill its allow-list.",
      "Answer 'Track all Nifty 500?' before BK-03 (derived baskets refresh job).",
      "Answer the 14 questions of the spec review of 2026-11-10 (docs/specs/REVIEW-2026-11-10.md; each has a recommendation): mainly BK order and BK-03 intraday baskets, BK-11 tiers, one capacity share, CF-04 B blocking AU-12, switching on the depth and tick recorders for P13, cutting basis, and how P8-16 measures load time.",
      "Read the ADRs marked Proposed (0065 to 0255; every slice was approved under the standing run instruction, not read by you). The SAFETY slices keep every new setting OFF until a deliberate act of yours.",
      "Run the pinned model fetch script when you want the assistant on (the download stays yours); run the owner probe for the expired-options backfill (P14-03) only if you want that backfill; the recorder, backfill, execution and assistant switches all stay OFF until you turn them on with a fresh password.",
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
"P11-01": ["ready", null, "Built 2026-10-28, all switches OFF (ADR 0149). To use it: capture one real trade book and order book answer (scrubbed) and give it to the agent to replace the made-up samples; set XD_LIVE_CAPTURE_ENABLED=true and XD_LIVE_CAPTURE_ORDER_BOOK=true (or switch live-capture and live-settle on in Services). Reads only: GET /v2/trades and GET /v2/orders; nothing places, changes or cancels an order."],
"P12-01": ["ready", null, "Built 2026-10-29 (ADR 0155). Nothing to switch on: the project holds no network code and nothing references it. The database, role and container are P12-01b."],
"P12-03": ["ready", null, "Built 2026-10-29 (ADR 0159), committed. Nothing to switch on: the checks are pure and the pre-send step still refuses every entry because no facts source exists yet (check_facts_unavailable). The migration AddExecutionChecks runs with the next xd-api migrate. Never run against real Dhan data."],
"P12-04": ["ready", null, "Built 2026-10-29 (ADR 0160), committed. Nothing to switch on: the protection monitor, watchdog, reduce-only exit policy, stop manager and square-off service are tested on the FakeBroker and composed by no host; the exit policy default stays closed. The migration AddExecutionProtection runs with the next xd-api migrate; GET /api/execution/alarms is read only. Stop-loss and super-order wire is UNVERIFIED and never run against real Dhan."],
"P12-05": ["ready", null, "Built 2026-10-30 (ADR 0161), committed. Nothing to switch on: the halt record and the owner's halt command are composed in xd-exec (they can only make things stricter) but the entry gate still reads the closed source, and the enforcer, drill and broker kill switch are composed nowhere. The migration AddExecutionHalts runs with the next xd-api migrate. To release a halt: POST /api/execution/halts/{id}/release needs Live__Writes__Enabled=true, your password and a reason. The Dhan kill switch and P&L exit are not built."],
"P12-07": ["ready", null, "Built 2026-10-30 (ADR 0163), committed. Nothing to switch on and no account can be promoted yet: the gate to assisted needs a sandbox suite result and a static-IP attestation that no module stores (both read unavailable), and the global execution switch has no API and stays OFF. The migration AddExecutionModes runs with the next xd-api migrate. Asking for a promotion (POST /api/execution/modes/{account}/promotion-requests) and confirming it need Live__Writes__Enabled=true and your password; stepping down needs neither. The mode source in xd-exec stays closed. The assisted sheet is split off as P12-07b."],
"P12-06": ["ready", null, "Built 2026-10-30 (ADR 0162), committed. Nothing to switch on: the order-update parser, applier, poll, reconciliation, recovery and break service are tested on the FakeBroker and an in-process fake transport, and composed by no host; there is no socket client. The migration AddExecutionReconciliation runs with the next xd-api migrate. To resolve a reconciliation break: POST /api/execution/breaks/{id}/resolve needs Live__Writes__Enabled=true and your password; a freeze caused by a break can then be released once every break of the account is resolved. The stream route is not built."],
"P12-02": ["ready", null, "Built 2026-10-29 (ADR 0157), sandbox only, committed. Nothing to switch on: the adapter exists only when the owner sets Execution__Environment=sandbox and creates the sandbox secret files; nothing calls it yet and live is refused. Never run against real Dhan."],
"P12-01b": ["ready", null, "Built 2026-10-29 (ADR 0156), committed. To start the execution service run deploy/xd-up.sh --exec (creates the Keychain item xdrishti/db/exec; recreates xd-db once for the new network). It stays environment none, heartbeats and answers a ping; no broker call exists."],
"P11-01b": ["ready", null, "Built 2026-10-29 (ADR 0154). Nothing to switch on: once live-capture and live-settle run, the journal links intraday live fills by itself. Intraday round trips show a fill grade with no R until intraday charges are read."],
"P11-02": ["ready", null, "Built 2026-10-28, switches OFF (ADR 0150). Nothing to do but wait for live sessions: the live-settle service measures each entry (so it needs live-capture and live-settle on) and the weekly live-estimates service (XD_LIVE_ESTIMATES_ENABLED=true or switch it on in Services) stores slippage per liquidity bucket and the capacity score; most buckets read insufficient for weeks. Reads stored rows only; nothing is applied to the simulator or the selection."],
"P11-03": ["ready", null, "Built 2026-10-29 (ADR 0152). Nothing to switch on: Live vs paper tab and xd live report read stored rows; it fills in as live tickets and their paper twins accumulate (a verdict needs 30 pairs). With more than one paper account set Live__Report__PaperAccountId."],
"P11-04a": ["ready", null, "Built 2026-10-28, all switches OFF. To use it: set XD_LIVE_WRITES_ENABLED=true, set S1 on the Ramp tab of Live, and the account switch to manual_live stays refused until the live-capture and live-settle services exist (P11-01)."],
"P11-05": ["ready", null, "Built 2026-10-28 (runbook in docs/research, Compliance tab, xd live compliance show). Start the enquiry first: the ISP lead time and Dhan's reply time are outside your control."],
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
        id: "P6", t: "Learning engine & ML.NET model", state: "built", note: "Everything is built, including regime labels (P6-02, which need stored index and VIX days). Models are advisory until P7; nothing is wired into selection.",
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
        id: "P7", t: "Selection, accounts & nightly plan", state: "built", note: "Paper only, Advisory by default, no orders, no reserve promotion (your 2026-10-23 answers). Auto options are built and OFF. Numeric ticket levels (P7-19) and crossing alerts (P7-20, OFF by default) are built. Depth recording (P7-16) and trade-tick recording (P7-16b) are built behind three default-off gates each and stay OFF until you switch them on. The ten-session exit needs the services switched on.",
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
          ["P7-16b", "Trade-tick recording (aggressive-volume source for P13) (built 2026-10-31, behind three default-off gates: second Quote-mode socket, your password switch, the paused service; market.trade_ticks has no reader; Services page switch built)", "built", "0173", {"d": 2, "o": 1}],
          ["P7-17", "Plan review screen (minimal) and Today tiles", "built", ""],
          ["P7-18", "The phase check: ten consecutive unattended sessions (reads NOT MET until the services have run)", "built", "0097"],
          ["P7-19", "Numeric ticket levels: entry reference, stop and target stored beside the text (built 2026-10-26)", "built", "0109", {"d": 2, "s": "P7-19-numeric-ticket-levels"}],
          ["P7-20", "Price-crossing alerts on armed tickets and post-trade prompts (built 2026-10-26; alerts are OFF by default)", "built", "0115", {"d": 3, "o": 1, "s": "P7-20-price-crossing-alerts-and-post-trade-prompts"}],
        ],
      },
      {
        id: "P8", t: "Reports & UI", state: "building", note: "Built: every read-only screen and report, the drill-down and trade replay, plan follow-ups, the audit log, the P8 phase check and the charge-sheet confirmation writer. P8-16 (responsive, accessibility, performance) is still partial: on 2026-11-09 first-load JS went down to 188.8 KB gzip and a failed lazy chunk now stays local, but C14 load time (median 5.4 s, all 94 cells above the 2x limit on a busy Mac) and 12 layout-shift cells still fail; reserved heights are the open work. P8-18 to P8-21 are built (switches OFF); they are not on the exit path.",
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
          ["P8-16", "Responsive, accessibility and performance pass over every P8 screen (partial: browser runner 2026-10-26; first-load JS cut to 188.8 KB gzip and lazy-chunk recovery 2026-11-09; C14 load time (all 94 cells above 2x) and 12 layout-shift cells still fail)", "blocked", "0124", {"d": 3.5, "s": "P8-16-responsive-accessibility-and-performance-pass"}],
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
        id: "P9", t: "AI assistant", state: "built", note: "Local assistant: off by default, every number checked against tool results, never decides a trade. P9-01 to P9-10 are built, switches OFF; the Hermes PoC is the container definition only, nothing third-party was fetched. The model download stays yours (a pinned fetch script).",
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
        id: "P11", t: "Small live", state: "building", note: "Manual live at 25-50% of intended risk; no order code. P11-01 to P11-06 are built, switches OFF (static-IP and SEBI record, ramp, fill capture, execution feedback, live versus paper, phase check). Exit needs at least 8 weeks of live sessions, so it reads NOT MET.",
        slices: [
          ["P11-05", "Static IP and SEBI re-check: runbook, questions and the compliance record (built 2026-10-28; the Compliance tab of Live writes only with XD_LIVE_WRITES_ENABLED=true and your password; no record written yet)", "built", "0147", {"d": 2, "o": 1, "s": "P11-05-static-ip-and-sebi-recheck-runbook"}],
          ["P11-04a", "Live enabling (first half of P11-04): ramp steps and scale, live pool in the plan, live-ready checklist gate, mode-switch gate, live auto-approval guard, Ramp tab (built 2026-10-28; all switches OFF; writes need XD_LIVE_WRITES_ENABLED=true and your password; the evidence gates, holds and halts are P11-04b)", "built", "0148", {"d": 2.5, "o": 1, "s": "P11-04-scale-up-rules-and-risk-ramp"}],
          ["P11-04b", "Scale-up rules (second half of P11-04): evidence gates G1 to G7, breach rules B1 to B7, automatic hold and step-down, daily and weekly halts with the loss measure, the evaluator, clear hold and exit confirmation as owner acts, notices, Today tile, xd live ramp show (built 2026-10-29; every switch OFF; the exit report P11-06 is not built so S3 stays closed; a halt invalidates no ticket, it is advice)", "built", "0151", {"d": 2.5, "o": 1, "s": "P11-04-scale-up-rules-and-risk-ramp"}],
          ["P11-01", "Manual-live fill capture: the day's trade book and order book read from Dhan (GET only, two entries added to the pinned allow-list), fills matched to approved tickets, one outcome per ticket, Capture tab (built 2026-10-28; the services start paused and the order book is off; the journal link of live fills is P11-01b)", "built", "0149", {"d": 3.5, "o": 1, "s": "P11-01-manual-live-fill-capture"}],
          ["P11-02", "Execution feedback: each live entry against its reference (delay and execution split), slippage per liquidity bucket with its n and intervals, the capacity score, the waterfall of a closed round trip, Execution tab (built 2026-10-28; weekly service paused and OFF; display only, nothing is applied; the journal-based fill grade waits for P11-01b)", "built", "0150", {"d": 3.5, "o": 1, "s": "P11-02-execution-feedback-grade-on-fills-slippage-and-capacity"}],
          ["P11-03", "Live versus paper report: paired tickets (shortfall in R split into entry and exit, interval, non-inferiority verdict), tickets left out with reasons, live slippage histogram, execution gap by strategy or month, Live vs paper tab (built 2026-10-29; read only, computed on read, no table; the pairing is tested on fakes)", "built", "0152", {"d": 3, "o": 1, "s": "P11-03-live-vs-paper-slippage-report"}],
          ["P11-01b", "Journal the live fills (deferred from P11-01): intraday live fills are linked to their tickets through a second key on the journal, never twice (delivery fills and fills with a stored twin are not read; the database refuses a double), a round trip with unrecorded charges is graded without an R, and the live vs paper report lists a doubted trip as live_uncertain (built 2026-10-29; no new setting, nothing calls a broker)", "built", "0154", {"d": 1, "o": 1, "s": "P11-01b-journal-live-fills"}],
          ["P11-06", "The small-live phase check (eight weeks, eight criteria and integrity line I): pure LivePhaseCheck, handler over the stored records, GET /api/live-trading/phase-check, xd live phase-check, Phase check tab; replaces NoExitReport so exit_confirmed reads the check (built 2026-10-29; S3 reachable only on a whole-report MET)", "built", "0153", {"d": 2, "s": "P11-06-small-live-phase-check"}],
        ],
      },
      {
        id: "P12", t: "Execution service (xd-exec)", state: "building", note: "Built in reduced form: skeleton, record and container, sandbox-only Dhan adapter, send journal, 34 pre-trade checks, protection, kill switch, reconciliation, readiness, confirm ticket, auto_live limits and the screens exist, but only paper, manual_live and assisted modes are composed; semi_auto and auto_live are not values and nothing places a live order. Exit is wall-clock: 10 assisted then 30 semi-auto sessions, zero reconciliation breaks.",
        slices: [
          ["P12-01", "xd-exec skeleton and the pure order state machine (built 2026-10-29: project XDrishti.Execution with the 23-move machine, leg ranks, client order id, account gate, position book, closed ports, in-memory FakeBroker, boundary tests; no network code, nothing reachable; database, role and container are P12-01b)", "built", "0155", {"d": 3, "o": 1, "s": "P12-01-xd-exec-skeleton-and-order-state-machine"}],
          ["P12-01b", "xd-exec record, role and container (built 2026-10-29: schema execution, xd_exec role and grants tests, database event store, command poller, heartbeat and status endpoint, Dockerfile target, compose xd-exec in profile live, xd-up.sh --exec; split from P12-01)", "built", "0156", {"d": 2, "o": 1, "s": "P12-01b-execution-record-role-and-container"}],
          ["P12-02", "Dhan order adapter, send protocol, FakeBroker and sandbox (built 2026-10-29, reduced: sandbox-only guarded Dhan client in XDrishti.Execution/Dhan, send protocol with in-memory journal, lookup-before-resend; live refused, nothing composed in the host; audit table, persistent journal and sandbox smoke are P12-02b)", "built", "0157", {"d": 3, "o": 1, "s": "P12-02-dhan-order-adapter-and-sandbox"}],
          ["P12-02b", "Persistent send journal, broker-call audit, settings wiring and read calls (built 2026-10-29: execution.broker_calls insert-only, PgSendJournal write-ahead and restart-proof counts, token expiry gate on three connection columns, caps/validity/collar/timeout read from settings, only LegSender composed behind sandbox + Execution:SendEnabled default OFF, four guarded GET reads, stateful FakeBroker and contract tests; no call to Dhan, sandbox smoke not built)", "built", "0158", {"d": 2, "o": 1, "s": "P12-02b-persistent-send-journal-audit-and-settings-wiring"}],
          ["P12-03", "Pre-trade checks (34 checks, 26 Hard and 8 Soft; unavailable is a reject; built 2026-10-29: pure ExecChecklist, ExecLimits, insert-only check_runs/check_results/check_confirmations, PgCheckStore, ChecklistPreSendCheck as the real IPreSendCheck; no facts source is wired, so every entry is still refused; read endpoint and fact loader moved)", "built", "0159", {"d": 2.5, "o": 1, "s": "P12-03-pre-trade-checks"}],
          ["P12-04", "Broker-side stops and the protection invariant (built 2026-10-29, reduced: pure ProtectionMonitor, StopRules, SquareOffPlan; ProtectionWatchdog with the breach ladder, ProtectionExitPolicy (reduce-only), StopManager (tighten, break-even, stop-unfilled), SquareOffService; insert-only execution.alarms and alarm_clears; GET /api/execution/alarms; unverified stop and super wire with fake-handler tests; nothing composed by a host, exit policy default still closed)", "built", "0160", {"d": 3, "o": 1, "s": "P12-04-broker-side-stops-and-protection"}],
          ["P12-05", "Kill switch and halts (built 2026-10-30, reduced: HaltRules 25-cell table, 19 trigger codes, pure TriggerEvaluator, HaltService as request door and gate (unreadable is halted), insert-only halts/halt_releases/halt_auto_clears/day_marks/drills/kill_switch_log with owner-release and auto-clear triggers, halt command in xd-exec, HaltEnforcer (cancel, exit_all via the square-off sequence), KillSwitchCoordinator and DrillService on fakes; GET/POST /api/execution/halts and owner release behind Live:Writes:Enabled and a fresh password; IHaltSource stays closed, nothing sends; Dhan kill switch, P&L exit, drill endpoints and evaluator loop moved)", "built", "0161", {"d": 2, "o": 1, "s": "P12-05-kill-switch-and-halts"}],
          ["P12-06", "Order update stream, crash recovery and reconciliation (built 2026-10-30, reduced: tolerant update parser, once-per-key rank-ruled applier, poll with gap fill and read budget, streams_alive, feed client over a fake transport only (no socket client, live host never returned), pure Reconciler with eight break kinds and a grace window, BreakService that only asks for a freeze, insert-only recon_runs/recon_breaks/break_resolutions with the owner resolution by xd_app and the freeze release rule, RecoveryService that reads before it writes and sends nothing, PollProtectionSource; GET /api/execution/reconciliation and POST /api/execution/breaks/{id}/resolve; nothing composed by a host; the stream route, the real socket, the schedulers and the journal-gap source moved)", "built", "0162", {"d": 3, "o": 1, "s": "P12-06-order-update-stream-and-reconciliation"}],
          ["P12-07", "Account execution modes: paper, manual live, assisted (built 2026-10-30, reduced: ladder of three rungs where semi_auto and auto_live are not values, six-check evidence gate with unavailable never passing (sandbox suite and static-IP attestation have no record yet so the gate is closed), owner request and confirm behind Live:Writes:Enabled and a fresh password with a 2-to-5-session window, owner and system demotions, insert-only mode_trail and promotion_requests with a trigger under a per-account lock, ModeTrailSource and ModeGuard composed nowhere; GET /api/execution/modes and three owner POSTs; the assisted sheet, ModeRouting, the settings catalogue and the allocation relaxation moved to P12-07b)", "built", "0163", {"d": 3.5, "o": 1, "s": "P12-07-account-execution-modes-paper-manual-assisted"}],
          ["P12-08", "Semi-auto: the one-tap confirm ticket, built in two parts (P12-08a intent creator and routing table; P12-08b confirm ticket and one-tap confirm; exact server-written text, decided once, judged by the database clock, older than 30 seconds sends nothing; a Soft-only rejection waits while a ticket is open; built 2026-11-01, composed nowhere that can send; semi_auto is not a mode value yet)", "built", "0165, 0166", {"d": 4, "o": 1, "s": "P12-08-semi-auto-one-tap-confirm"}],
          ["P12-09", "auto_live within hard limits (built 2026-11-01, reduced: per-account limits sheet proved by the database, an empty sheet allows nothing; composed nowhere and auto_live_enabled is refused by the API; nothing sends)", "built", "0167", {"d": 3, "o": 1, "s": "P12-09-auto-live-within-hard-limits"}],
          ["P12-10", "Execution screens and notifications (built 2026-11-01: GET-only read models, five tabs by URL with caps meters and readiness, notifications; says unavailable rather than zero; includes the owner's execution switch and the kill-switch drill cards on the Modes tab, ADR 0174; the switch is OFF)", "built", "0168, 0174", {"d": 3, "o": 1, "s": "P12-10-execution-screens-and-notifications"}],
          ["P12-11", "Daily login, token and static-IP operations: readiness (built 2026-11-01, reduced: fail-closed readiness verdict per account, owner IP attestation, Dhan profile and static-IP reads through the guarded sandbox client only; live readiness stays unavailable until a live token source is in use; sealed token hand-over and a host-pinned forward proxy are built, ADR 0171 and 0172, behind switches OFF)", "built", "0164, 0170, 0171, 0172", {"d": 2.5, "o": 1, "s": "P12-11-daily-login-token-and-static-ip-operations"}],
          ["P12-B1", "Ticket-state bridge: write Triggered and Closed on plan tickets from execution", "later", "", {"d": 1.5}],
          ["P12-12", "The phase check: 30 consecutive proved semi-auto sessions per account (built 2026-11-01, read-only measurement from stored rows with a phase check panel on the Modes tab; reads NOT MET or NOT EVALUABLE on thin data; semi-auto itself is not composed, so it cannot read MET yet)", "built", "0169", {"d": 2, "s": "P12-12-phase-check-30-semi-auto-sessions"}],
          ["P12-13", "Swing, partial-exit and pyramid execution (out of P12, UNSIZED)", "later", "", {}],
          ["P12-14", "Order-group concept for two-leg pairs (UNSIZED, before any live PAIRS)", "later", "", {}],
        ],
      },
      {
        id: "P13", t: "Tick & depth features", state: "planned", note: "Only P13-05 is built (ADR 0183). The depth and tick recorders exist (P7-16, P7-16b) but ship OFF; the other nine slices (about 20 agent-days, reviewed 2026-11-10) wait for about 11 months of recordings from the day you switch the recorders on.",
        slices: [
          ["P13-01", "Recording coverage and capacity report", "approved", "", {"d": 2, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-02", "Book reader and as-of reconstruction", "approved", "", {"d": 2.5, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-03", "Feature catalog md-1 and its extraction (point in time)", "approved", "", {"d": 3, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-04", "Depth slippage estimator", "approved", "", {"d": 2.5, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-05", "Feature-set comparison core (shared with P15-10)", "built", "0183", {"d": 3, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-06", "Evaluation run and verdict record", "approved", "", {"d": 2, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-07", "Shadow challenger and your keep/drop", "approved", "", {"d": 2, "o": 1, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-08", "Depth-aware pre-trade check (cut line)", "approved", "", {"d": 3, "o": 1, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-09", "Microstructure report in the Reports and Learning screens", "approved", "", {"d": 2, "s": "P13-00-tick-and-depth-features-plan"}],
          ["P13-10", "P13 phase check: months recorded, folds, n, verdict", "approved", "", {"d": 1, "s": "P13-00-tick-and-depth-features-plan"}],
        ],
      },
      {
        id: "P14", t: "Options & statistical arbitrage", state: "building", note: "Built except the basis pair: P14-01 to P14-13, P14-16 and P14-17 are built (2026-11-01 to 2026-11-09). Option pricing, the expression dimension, the cost model, the option and defined-risk simulators, the IV features, pairs core and study, the reports and the Options and stat-arb tab exist; recorders and backfill stay behind default-off gates. P14-14 and P14-15 (basis data and carry) were dropped 2026-11-13 (ADR 0289). `xd options phase-check` reads stored verdicts only and cannot read MET until the archive holds real chains and a family earns a net KEEP; the exit is decided by the six families. Research only: no order, no margin call.",
        slices: [
          ["P14-01", "Option contracts, lot-size history, expiry and rule epochs (built 2026-11-01, inside the master refresh; the rule epochs are seeded unconfirmed)", "built", "0175", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-02", "Chain snapshot recorder (built 2026-11-01, behind three default-off gates; two new allow-listed data-api endpoints; paused service; no reader; Services-page switch built)", "built", "0176", {"d": 3, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-03", "Expired-options backfill (built 2026-11-01, behind four default-off gates including your stored acceptance of an owner-run probe that was never run against Dhan; one new allow-listed endpoint; no reader; Services-page switch built)", "built", "0177", {"d": 3, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-04", "Archive quality checks (built 2026-11-01, on by default because it only reads the archive; insert-only verdicts, unavailable never counts as pass; read-only Options archive card on Data quality)", "built", "0178", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-05", "Option pricing core (pure): Black-76 and Black-Scholes, implied-volatility solver, Greeks (built 2026-11-03)", "built", "0191", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-06", "Instrument-expression dimension: the cell key gains reserved universe and expression parts (built 2026-11-03)", "built", "0193", {"d": 2.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-07", "Option cost and liquidity model (optional option and futures lines on your charge sheet) (built 2026-11-03)", "built", "0194", {"d": 2.5, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-08", "Option expression simulator (each stored cash trade re-marked as one long option) (built 2026-11-04)", "built", "0197", {"d": 3.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-09", "Expression evaluation and verdict (protocol O, paired out-of-sample against cash) (built 2026-11-04)", "built", "0198", {"d": 3, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-10", "Defined-risk structures: debit spread, credit spread, iron condor (index only, range regime) (built 2026-11-04)", "built", "0200", {"d": 3.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-11", "IV and event features (extension iv-1, read at the decision instant) (built 2026-11-04)", "built", "0199", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-12", "Pairs core (pure): log spread, OLS hedge ratio, Engle-Granger with pinned MacKinnon tables (built 2026-11-03)", "built", "0192", {"d": 3, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-13", "Pairs simulation and evaluation (same-sector intraday cash pairs, every candidate in the ledger) (built 2026-11-03)", "built", "0196", {"d": 3.5, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-14", "Basis data and carry model (dropped, ADR 0289)", "later", "", {"d": 2.5, "o": 1, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-15", "Basis evaluation (dropped, ADR 0289)", "later", "", {"d": 2, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-16", "Reports: archive summary, expression runs, pair formations, verdicts; Options and stat-arb tab (part B 2026-11-09) (built 2026-11-09)", "built", "0201", {"d": 3, "s": "P14-00-options-and-stat-arb-plan"}],
          ["P14-17", "P14 phase check (xd options phase-check; reads stored verdicts only) (built 2026-11-04)", "built", "0202", {"d": 1.5, "s": "P14-00-options-and-stat-arb-plan"}],
        ],
      },
      {
        id: "P15", t: "Fundamentals & events", state: "built", note: "All twelve slices are built and deployed (2026-11-03); the phase check (xd events phase-check) reads NOT EVALUABLE until you supply real filings, the announcement labels and the 5-year history (CF-04). Every switch stays OFF.",
        slices: [
          ["P15-01", "Filing sources and the terms gate (built 2026-11-01, reduced: folder drop only, read-only adapter with a strict sidecar; the public fetcher is recorded as not built, no network code)", "built", "0179", {"d": 3, "o": 1, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-02", "Point-in-time filing store (built 2026-11-01, insert-only schema filings; visible once broadcast time plus lag has passed; undated is unavailable; ingest service registered paused; metadata endpoints only, no reader in any decision code)", "built", "0180", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-03", "Results XBRL parser (pure)", "built", "0184", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-04", "Calendar and shareholding", "built", "0181", {"d": 2, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-05", "Features ev-1 and fu-1", "built", "0185", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-06", "Event-risk filter and loss tag", "built", "0182", {"d": 2, "o": 1, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-07", "Announcement eval set and labelling (you label about 200 announcements)", "built", "0188", {"d": 2, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-08", "LLM announcement flags and field extraction (schema-checked, never decides trades)", "built", "0189", {"d": 3, "o": 1, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-09", "PEAD strategy and the longer swing hold (max_hold up to 20)", "built", "0187", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-10", "Evaluation and verdicts", "built", "0186", {"d": 3, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-11", "Screens", "built", "", {"d": 2, "s": "P15-00-fundamentals-and-events-plan"}],
          ["P15-12", "P15 phase check", "built", "0190", {"d": 1, "s": "P15-00-fundamentals-and-events-plan"}],
        ],
      },
      {
        id: "AU", t: "Automation & simplicity", state: "built", note: "All 19 slices built 2026-11-09 and 2026-11-10 (ADRs 0240 to 0253), on your request of 2026-11-09: missed days are recovered automatically on start, wake or reconnect and proved in the stored data (Recovery screen); 11 certain unused items removed; Settings opens on five essentials, learned changes only tighten and loosening needs your password; simulate-everything trains on fenced data that never counts as evidence (Simulation report); one Auto switch (OFF, password to turn on) runs the chain and only suggests. No orders. `xd auto check` reads NOT EVALUABLE until the services have run on real days.",
        slices: [
          ["AU-00", "Programme plan (built 2026-11-09)", "built", "0240–0244", {"d": 0.5, "s": "AU-00-automation-and-simplicity-plan"}],
          ["AU-01", "Recovery planner (pure): every service declares a recovery class (built 2026-11-09)", "built", "0240", {"d": 2, "s": "AU-01-recovery-planner-pure"}],
          ["AU-02", "Recovery runner and its three triggers (start, wake, reconnect) (built 2026-11-09)", "built", "0240", {"d": 3, "s": "AU-02-recovery-runner-and-triggers"}],
          ["AU-03", "Multi-day data proof: a recovered day is proved in the stored data (built 2026-11-09)", "built", "0240", {"d": 1, "s": "AU-03-recovery-of-multi-day-data-proof"}],
          ["AU-04", "Recovery screen and one audited retry (built 2026-11-09)", "built", "", {"d": 2, "s": "AU-04-recovery-screen"}],
          ["AU-05", "Unused-code audit and the recorded removal list (built 2026-11-09)", "built", "0245", {"d": 2, "s": "AU-05-unused-code-audit-and-removal-list"}],
          ["AU-06", "Remove unused backend code (eight members) (built 2026-11-09)", "built", "0245", {"d": 2, "s": "AU-06-remove-unused-backend"}],
          ["AU-07", "Remove unused frontend code and settings keys (three items) (built 2026-11-09)", "built", "0245", {"d": 1.5, "s": "AU-07-remove-unused-frontend-and-config"}],
          ["AU-08", "Settings catalog and visibility tiers (five Essential settings) (built 2026-11-09)", "built", "0246", {"d": 2, "s": "AU-08-settings-inventory-and-tiers"}],
          ["AU-09", "Simple settings screen (five essentials, Advanced reveal; AC3 and AC4 delivered by AU-10, AU-16, AU-17) (built 2026-11-09)", "built", "", {"d": 2, "s": "AU-09-simple-settings-screen"}],
          ["AU-10", "Learned tightening; loosening or undo needs your password (built 2026-11-10)", "built", "0247", {"d": 3, "o": 1, "s": "AU-10-learned-tightening-with-password-to-loosen"}],
          ["AU-11", "Training-simulation record and evidence fence (built 2026-11-10)", "built", "0243", {"d": 2, "s": "AU-11-training-simulation-record-and-evidence-fence"}],
          ["AU-12", "Simulate-everything job, resumable, yields to market hours (built 2026-11-10)", "built", "0248", {"d": 3, "s": "AU-12-simulate-everything-job"}],
          ["AU-13", "Walk-forward training from the simulation (hold-out kept out whole) (built 2026-11-10)", "built", "0249", {"d": 3, "s": "AU-13-walk-forward-training-from-simulation"}],
          ["AU-14", "Simulation report API (labelled not evidence) (built 2026-11-10)", "built", "0250", {"d": 2, "s": "AU-14-simulation-report-api"}],
          ["AU-15", "Simulation report screen (built 2026-11-10)", "built", "", {"d": 2, "s": "AU-15-simulation-report-screen"}],
          ["AU-16", "Auto scope and reasoned next-day suggestions (built 2026-11-10)", "built", "0251", {"d": 3, "s": "AU-16-auto-scope-and-reasoned-suggestions"}],
          ["AU-17", "The Auto switch (password ON, fail-closed on unconfirmed charges) (built 2026-11-10)", "built", "0252", {"d": 3, "o": 1, "s": "AU-17-the-auto-switch"}],
          ["AU-18", "Auto and next-day suggestions screen (no order controls) (built 2026-11-10)", "built", "", {"d": 2, "s": "AU-18-auto-suggestions-screen"}],
          ["AU-19", "AU programme check (xd auto check) (built 2026-11-10)", "built", "0253", {"d": 1, "s": "AU-19-programme-check"}],
        ],
      },
      {
        id: "BK", t: "System baskets", state: "building", note: "Under way: BK-02, the pure derived-baskets engine (14 definitions, point in time, versioned params), is built (2026-11-10, ADR 0255); BK-01 (index catalogue and NSE fetcher, OFF) is in build on its own branch. The rest is Approved and not started. NSE fetches (BK-01, BK-05) and the derived-basket job (BK-03) stay OFF until you enable them; BK-08 and BK-10 change the fill and sizing (SAFETY, every limit default OFF, advisory only). Answer 'Track all Nifty 500?' before BK-03. Review 2026-11-10: about 43 agent-days left; recommended order BK-01, BK-05, BK-03, BK-04, then BK-06 onward.",
        slices: [
          ["BK-00", "Programme plan", "approved", "", {"d": 0.5, "s": "BK-00-system-baskets-plan"}],
          ["BK-01", "Index basket catalogue, NSE fetcher (OFF), versioned membership (in build on branch feature/bk-01, not merged)", "approved", "", {"d": 4, "o": 1, "s": "BK-01-index-basket-catalogue-fetcher-and-versioned-membership"}],
          ["BK-02", "Derived baskets engine (pure): indicator kit, 14 definitions, rank selector with buffer and dwell (built 2026-11-10)", "built", "0255", {"d": 4, "s": "BK-02-derived-baskets-engine-pure"}],
          ["BK-03", "Derived baskets refresh job and scores", "approved", "", {"d": 4, "o": 1, "s": "BK-03-derived-baskets-refresh-job-and-scores"}],
          ["BK-04", "System baskets on the Baskets screen", "approved", "", {"d": 3, "s": "BK-04-system-baskets-on-the-baskets-screen"}],
          ["BK-05", "Broker flag and F&O baskets", "approved", "", {"d": 3, "o": 1, "s": "BK-05-broker-flag-and-fo-baskets"}],
          ["BK-06", "Basket index and profile metrics engine (pure)", "approved", "", {"d": 4, "s": "BK-06-basket-index-and-profile-metrics-engine-pure"}],
          ["BK-07", "Basket profile endpoints and screen", "approved", "", {"d": 7, "s": "BK-07-basket-profile-endpoints-and-screen"}],
          ["BK-08", "Correlation clusters and caps, basket breadth in the market gate", "approved", "", {"d": 5, "o": 1, "s": "BK-08-correlation-clusters-and-caps-and-basket-breadth-in-the-market-gate"}],
          ["BK-09", "Relative-strength features and sector strength", "approved", "", {"d": 5, "s": "BK-09-relative-strength-features-and-sector-strength"}],
          ["BK-10", "Volatility-targeted sizing and basket risk budgets", "approved", "", {"d": 5, "o": 1, "s": "BK-10-volatility-targeted-sizing-and-basket-risk-budgets"}],
          ["BK-11", "Universe cell dimension and strategy-universe link", "approved", "", {"d": 4, "s": "BK-11-universe-cell-dimension-and-strategy-universe-link"}],
          ["BK-12", "AI basket tools", "approved", "", {"d": 2, "o": 1, "s": "BK-12-ai-basket-tools"}],
        ],
      },
      {
        id: "CF", t: "Confidence programme", state: "building", note: "Built (2026-11-03 to 2026-11-09) except CF-04 Part B: effective sample size, shrinkage, deflated Sharpe and PBO, cost stress and conservative slippage, pre-registration with the trial ledger and locked hold-out, random-entry null, placebo and plateau gates, shadow paper trading with its digest, the sequential failure test, ML safety (calibration, ladder, influence gate, champion/challenger and rollback), the readiness scorecard with its sources, and the decay monitor with kill proposals. Most of it is report-only; every Gate mode, automatic rollback and automatic demotion stays OFF until you switch it. CF-04 Part A (NSE daily history, fetch OFF behind an empty allow-list) is built; Part B (Dhan 1-minute backfill) is open. Nothing has run on your real data yet.",
        slices: [
          ["CF-00", "Programme plan and slice map", "approved", "", {"d": 0.5, "s": "CF-00-confidence-programme-plan"}],
          ["CF-01", "Effective sample size, power and minimum track record (built 2026-11-04)", "built", "0203", {"d": 5.5, "s": "CF-01-effective-sample-size-power-and-minimum-track-record"}],
          ["CF-02", "Empirical-Bayes shrinkage and parent-child pooling (built 2026-11-04)", "built", "0204", {"d": 4, "s": "CF-02-empirical-bayes-shrinkage-of-expectancy-and-parent-child-pooling"}],
          ["CF-03", "Deflated Sharpe, PBO and block bootstrap (built 2026-11-04 to 11-08)", "built", "0205, 0228", {"d": 7, "s": "CF-03-deflated-sharpe-pbo-and-block-bootstrap"}],
          ["CF-04", "History backfill to five years (Part A NSE daily history built 2026-11-03, fetch OFF behind an empty allow-list; Part B Dhan 1-minute backfill open)", "approved", "0195", {"d": 10.5, "o": 1, "s": "CF-04-history-backfill-to-five-years-daily-bhavcopy-and-corporate-actions"}],
          ["CF-05", "Cost stress gate and fill-one-minute-later test (built 2026-11-04 to 11-09)", "built", "0206, 0229, 0230", {"d": 5.5, "s": "CF-05-cost-stress-gate-and-fill-one-minute-later-test"}],
          ["CF-06", "Pre-registration, trial ledger and locked hold-out in the Lab (built 2026-11-05)", "built", "0207", {"d": 6, "o": 1, "s": "CF-06-preregistration-trial-ledger-and-locked-holdout-in-the-lab"}],
          ["CF-07", "Random-entry null, placebo runs and plateau gate (built 2026-11-05 to 11-09)", "built", "0208–0210, 0231", {"d": 5.5, "s": "CF-07-random-entry-null-placebo-runs-and-plateau-gate"}],
          ["CF-08", "Shadow paper trading of every nightly candidate (built 2026-11-05 to 11-09)", "built", "0211, 0212, 0232", {"d": 6, "o": 1, "s": "CF-08-shadow-paper-trading-of-every-nightly-candidate"}],
          ["CF-09", "Sequential failure test for paper trading (built 2026-11-05)", "built", "0213", {"d": 3, "s": "CF-09-sequential-failure-test-for-paper-trading"}],
          ["CF-10", "ML safety: calibration, ladder, champion/challenger rollback (built 2026-11-05 to 11-07)", "built", "0214–0217, 0220", {"d": 13.5, "o": 1, "s": "CF-10-ml-safety-calibration-ladder-and-champion-challenger-rollback"}],
          ["CF-11", "Readiness scorecard, per strategy and system (built 2026-11-06 to 11-08)", "built", "0221–0227", {"d": 7, "o": 1, "s": "CF-11-readiness-scorecard-per-strategy-and-system"}],
          ["CF-12", "Decay monitor, kill rules, is the learning loop helping (built 2026-11-06 to 11-09)", "built", "0218, 0219, 0234", {"d": 9, "o": 1, "s": "CF-12-decay-monitor-kill-rules-and-is-the-loop-helping"}],
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
