# Prototype update plan (2026-10-25)

Source: `docs/specs/JOURNEY.md` and the specs written 2026-10-24 (all **Draft**). The prototype is the UX reference; every screen below carries a small `Spec P#-## (Draft)` tag so a reader knows it is specified, not built. Demo data only; nothing here places an order.

Rules every new screen shows (P8-00 D1/D3, P9-00, P12): honest states (icon + word + reason; never a number for `insufficient` / `unavailable` / `not_evaluable` / `empty`, never green), an evidence block with limits on every report, every write or forward-looking control shows its SAFETY default OFF and the owner's password step-up, no composite score, nothing implies an order before the execution phase.

New code lives in `assets/app-pages-5.js` (shared kit `BT.kit`, Assistant), `assets/app-pages-6.js` (Paper & live, Execution, app-bar stop chip, bell notices) and `assets/app-pages-7.js` (wrappers that add the spec'd parts to existing pages without rewriting them). Nav, icons and shortcuts edited in `app-core.js` / `ui.js`; styles appended to `proto.css`; `?v=` bumped to 18.

## Build order and status

| # | Page / area | What is added | Source spec | Status |
|---|---|---|---|---|
| 1 | Shared kit (`app-pages-5`) | `specTag`, honest-state cell (`stateCell`), `evidence` block, `lockedSwitch` (default OFF + password step-up), `Phase` chips | P8-00, P8-01 | done |
| 2 | **Assistant** (new page) | gate states (off / model down / loading / tools down / busy / ready) with a state switcher, evaluation line, streamed chat with tool-call citation chips and expandable rows, grounding marks and banner, plain-text rendering, history list/drawer, settings dialog (owner act, password), stop, example prompts | P9-04 | done |
| 3 | Suggestions | Proposals panel: kind, reason, Evidence block, engine checks (word + icon), expiry, Accept/Reject with exact-effect confirm, auto-accept switches (OFF, password) | P9-08 | done |
| 4 | Plan review | Briefing card (source label, words, facts used, why model text rejected); structured plan flags chips; "no plan published, and why" run banner; ticket chart table fallback note; armed-ticket crossing alerts and post-trade prompts | P9-06, P8-14, P7-20 | done |
| 5 | Strategy Lab | "Draft with the assistant" panel: idea, understood / assumptions / not expressed, validate result, Confirm needs owner (universe, password), decline path | P9-07 | done |
| 6 | **Paper** (new page) | tabs Ledger, Review, Discipline, Backtest vs paper, Governance, Phase check, Live (placeholder states); account picker, epoch banner, gross/net honesty, no write control | P10-02/03/04/05/06, P11-01/03/04 | done |
| 7 | **Execution** (new page, "Phase P12, off by default") | tabs Today, Orders, Positions, Activity, Modes; mode ladder with gates and promotion request/confirm (password twice, cooling off), limits form, switches, drill; semi-auto confirm ticket card (90 s, 800 ms arm delay, text verbatim); readiness card and morning checklist; static IP attestation | P12-07/08/10/11 | done |
| 8 | App bar + bell (`app-core.js` hooks, logic in `app-pages-6`) | Stop chip and dialog (three outcomes, exit-all needs step-up), plan and execution notices merged into the bell with severity word + icon | P12-10, P8-15 | done |
| 9 | Accounts | Trading tab: mode select limited to paper / manual_live with "Automated modes are changed on the Execution screen"; ramp step and halt state; Connection tab readiness card | P12-07, P11-04, P12-11 | done |
| 10 | Learning | Journey: counterfactual headline (`Inconclusive`), composite health ring replaced by per-line states; Smart rules: five rules with `not_evaluable`, loss tags with R lost, entry windows; Tweak & re-run: capability OFF, preview, "can at best be Inconclusive" box; Lessons/Autonomy: owner-act banner; tiles Live expectancy, Uplift, Execution gap now `not_evaluable` | P8-12/13/18/19, P8-02 | done; Plan vs actual, Decisions and Setups tabs left as the demo (not rewritten) |
| 11 | Reports | Evidence block + limits under every report, exports (CSV/HTML) default OFF with evidence preamble and formula-injection guard, trade replay drawer (chart, events, features with `unavailable` context, bars table) | P8-11, P8-20, P8-08 | done; pivot builder and catalogue pointer entries (P8-09/10) not rebuilt |
| 12 | Settings | Owner-act capabilities card (all OFF), forms for hypothesis move / lesson close / revert, phone and install card (OFF), alerts switch | P8-19/20/21, P7-20 | done |
| 13 | Audit | Six sources with "N of 6 could not be read", filters, summary line, detail drawer, redaction mark | P8-22 | done |
| 14 | Today | Execution status strip ("Execution is off", Phase P12) | P12-10 | done; halt tile not added (halts show on Execution and in the app bar) |

Not built, on purpose: backend-only behaviour (hub events, SSE parser, retention, polling intervals) appears only as its visible state; the install prompt itself and a real service worker (P8-21) are described, not implemented; `docs/prototype/delivery` was not touched.

Prototype-only controls (dashed bars labelled "Prototype only") let a reader pretend a status (assistant gate, execution sim, nightly failed closed, one audit source unreadable). They are not part of the specs.
