# First-evidence runbook — from deploy to the first verdicts

The ordered owner steps that take a freshly deployed stack to its first readable verdicts. Every command and screen
below was checked against main at 30777841 (2026-11-12); the file that proves it is in brackets. Anything not proven
is marked **UNVERIFIED**. Nothing here places an order; every switch named is OFF by default and turning it on is
your act. An `unavailable` input never counts as a pass anywhere in the system.

Conventions: `xd …` means `scripts/xd.sh …` (it runs the `xd` verb on the host and reads only the database password
from the Keychain [scripts/xd.sh]). The stack must be up for every verb. Screens are in the web app at
`http://127.0.0.1:8080` [deploy/xd-up.sh: `XD_HTTP_PORT`].

**Step 0 — readout (not built yet).** `xd evidence preflight` will print MET / NOT MET / UNAVAILABLE for each
prerequisite below (spec [EV-01](../specs/EV-01-evidence-preflight.md)). Until it is built, use the per-step checks.

## 1. Deploy
- `deploy/xd-up.sh` (options `--no-dev`, `--no-build`, `--owner-reset`; never `--exec` for this runbook) [deploy/xd-up.sh:11].
- Check: System screen `/system`, or `GET /api/system/status` → `database.pendingMigrations = 0` [GetSystemStatus.cs:19].
  The image tag is always `0.1.0` unless you set `XD_VERSION` [deploy/compose.yaml:99], so the tag proves nothing.

## 2. Read and accept the safety ADRs
Read, then mark Accepted (they are Proposed until you have read them):
[0207](../adr/0207-a-strategy-version-is-pre-registered-the-ledger-counts-every-extra-look-and-a-locked-hold-out-is-fixed-at-the-first-post-and-read-once-by-an-owner-unlock-with-enforcement-and-the-honest-count-as-the-owners-switches.md) pre-registration and the hold-out,
[0217](../adr/0217-a-model-may-influence-a-decision-only-through-one-gate-whose-predicate-needs-an-unspent-hold-out-forward-evidence-no-calibration-alarm-and-the-owners-switch.md) the ML influence gate,
[0252](../adr/0252-the-auto-switch-resumes-five-chain-services-follows-the-stored-switch-for-the-builder-and-restores-only-what-it-resumed.md) the Auto switch,
[0272](../adr/0272-the-system-replay-is-research-only-fenced-from-evidence-and-hindsight-labelled-and-runs-the-live-pieces-over-as-of-facts-in-memory.md) the system replay,
[0273](../adr/0273-pre-seam-bars-enter-a-run-only-for-an-instrument-proven-verified-through-a-stored-basis-with-the-switch-off-by-default-and-the-seam-by-date.md) verified pre-seam bars.
How "accept" is recorded (Status line edit by you) follows the ADR README; **UNVERIFIED** whether any check reads it
other than `xd auto check`, which is given `Auto__Check__AdrFolder` [scripts/xd.sh: `export Auto__Check__AdrFolder`].

## 3. Charge sheet (ends "costs: none")
1. Write the sheet JSON (doc 16 §2 shape) from Dhan's current tariff, or later propose one from your reports with
   `cost-sheet derive` (spec [CS-01](../specs/CS-01-cost-sheet-derive-from-dhan-reports.md), not built).
2. `scripts/cost-sheet.sh load sheet.json` [scripts/cost-sheet.sh:3].
3. `scripts/cost-sheet.sh check <id> <report1.csv> [<report2.csv> …]` against your ORIGINAL Dhan global transaction
   report files; writes nothing; every row within ₹0.05 or listed [scripts/cost-sheet.sh:6; CostBillCheck.cs:15].
4. Settings → Advanced → **Charge sheet** → "Allow confirming" (fresh password), then **Confirm** (fresh password)
   [AdvancedSettings.tsx:21; ChargeSheetActions.tsx:75-99].
5. Check: `xd charge-sheet show [--sheet ID]` says the confirmation stands [ChargeSheetCommand.cs:37].

## 4. Context probe (regime, VIX and the market gate)
1. Instruments screen `/instruments`: track **NIFTY 50**, **INDIA VIX** and **Nifty 500** (the index).
   Steps and the reason are in [docs/research/p3-01d-context-probe.md](../research/p3-01d-context-probe.md) §Steps.
2. Data screen `/data` → **Update data** (or wait for the afternoon end-of-day job) [p3-01d §Steps 3].
3. `scripts/context.sh coverage` (add `scripts/context.sh refresh` if days are missing) [scripts/context.sh:3-4].
4. Write the results and the outcome O1–O5 into docs/research/p3-01d-context-probe.md §Results (by hand).
5. Settings → Advanced → **Limits** → basket settings: the market gate reads breadth from **Nifty 500**
   [LimitsCard.tsx → BasketSettingsSection.tsx; BasketSettingsForm.tsx:11]. The Nifty 500 *basket* exists only after
   the system-basket fetch has run: read the NSE terms, accept ADR 0254, set `Baskets:System:Fetch:Enabled=true` and
   `Baskets:System:Fetch:UserAgent` (ADR 0254:20,37). These two have **no compose variable** today
   [BasketFetchOptions.cs:13-20; deploy/compose.yaml] — spec [OPS-01](../specs/OPS-01-evidence-switches-reachable.md)
   adds them. Until then you must add them to `xd-api`'s environment in deploy/compose.yaml yourself.

## 5. Decide when to fix the hold-out
The locked hold-out is fixed **once, at the first Lab post of any method** (a quick test counts), as 20 % of the
stored history and at least 6 months, and never moves earlier [ADR 0207 §3]. So: if you will load more history
(step 6), do it **before** any Lab test. Check: `GET /api/lab/holdout` [LabConfidenceEndpoints.cs:33].

## 6. Optional: more history (before step 7)
1. NSE daily files: read NSE's terms, accept ADR 0195; add the archive patterns to
   `MasterDataEndpoints.Patterns` and the matching architecture-test change (ADR 0195:26-30, a code edit).
2. Probe, then load, with the switch on for the host verb:
   `Data__History__Nse__Enabled=true xd data eod-probe --dates 2024-01-02,2024-06-03`, then
   `Data__History__Nse__Enabled=true xd data eod-load --from 2016-01-01 --to 2026-11-11` [DataEodCommand.cs:11-12,113-120].
   The environment-variable override for the host verb is standard .NET binding; that `xd-api`'s `history-eod`
   service also needs the switch, which has **no compose variable** (OPS-01) [HistoryOptions.cs:47; compose.yaml:138-139].
3. Dhan 1-minute backfill (ADR 0270:30): `Data__History__Backfill__Enabled=true xd data backfill --dry-run`, then
   `… xd data probe-seam`, then `… xd data backfill` (or resume the `history-backfill` service on `/services`).
   `xd data history` shows reach, state and `verified=` per instrument [DataBackfillCommand.cs:17-22].
   **UNVERIFIED:** whether the host verb can read the Data account's token — `scripts/xd.sh` exports only the
   database password, while other Dhan-reading scripts need the broker vault key [scripts/verify-pack.sh:60]. If the
   host verb fails on the token, use the `history-backfill` service in `xd-api` (needs the compose switch, OPS-01).
4. Admit verified pre-seam bars into runs: `Data:History:Replay:AdmitVerified=true` for `xd-api` (ADR 0273; no compose
   variable yet, OPS-01) [HistoryOptions.cs:41]. Flip it between jobs, never during one.

## 7. Pre-register, then run the Lab full tests
1. Lab screen `/lab` → the version's **Registration** card (who is on the other side, expected signs, tiers)
   [RegistrationCard.tsx; `PUT /api/lab/strategies/{id}/versions/{v}/registration`, LabConfidenceEndpoints.cs:19].
2. `xd strategy test my.yaml --symbols 'RELIANCE,TCS' --method full` (or `--basket-id GUID`; no `--from/--to` so the
   default window stops before the hold-out) [XdOptions.cs:80-189]. Exit 0 PASS, 4 MARGINAL/FAIL [XdCommand.cs:13].
   The first post fixes the hold-out (step 5).
3. `xd lab runs --strategy ID` lists the runs and their keys.

## 8. Cell snapshots
`xd learn cells --as-of YYYY-MM-DD --run RUNKEY:STYLE:EXIT` per run (e.g. `RUNKEY:intraday:FIXED_RR`)
[LearnCellsOptions.cs:21,61-70]. No service writes cell snapshots; only this verb does.

## 9. Retrain and designate the champion
`xd learn training-set --run RUNKEY:STYLE:EXIT`, `xd learn model --run RUNKEY:STYLE:EXIT`, then
`xd learn designate --model N --role champion --reason "…"` (only an Eligible model) [scripts/xd.sh header;
LearnDesignateOptions.cs:44-57]. Today the ML gate refuses every model (ADR 0217), so a champion influences nothing.

## 10. System replay (research only)
Screen `/simulation/replay` [navigation.ts:78]. At the default `Replay:MaxWindowMb` 300 the largest scope is
**53 instruments**; raise it only together with `xd-api`'s `mem_limit` (1 g) [ADR 0272:49; compose.yaml:194]. A start
is refused (`replay.busy`) while the history backfill is incomplete. A replay is never evidence (ADR 0272).

## 11. Forward evidence
1. Shadow record ON: `PUT /api/paper/shadow/settings` with `{enabled:true, reason, password}`, a signed-in session
   and the `X-CSRF-Token` header [PaperShadowEndpoints.cs:23; ShadowSwitchRequest.cs:8; SessionAuthentication.cs:11].
   There is **no UI control** yet (Paper → Shadow shows ON/OFF only, ShadowTab.tsx:59); OPS-01 adds the form. A
   copy-paste request recipe is **UNVERIFIED** (not written here: it would put your password on a command line).
2. Services screen `/services`: resume `shadow-write`, `shadow-grade` (the switch only allows them; they start
   paused), `shadow-digest` and `paper-capture` [ShadowWriteService.cs:16; ShadowGradeService.cs:17;
   ShadowDigestService.cs:15; PaperCaptureService.cs:16].
3. Auto screen `/auto` → set the scope, then **Auto ON** (fresh password). Preconditions: scope set, Data account
   connected, charge sheet confirmed, every chain service registered [ADR 0252 §2]. Auto resumes only `eod`,
   `portfolio-broker-history`, `weekly`, `nightly`, `suggestions` [ADR 0252 §1], **not** the step-2 services.
4. Until LC-01 is built ([spec](../specs/LC-01-scheduled-cell-statistics-refresh.md), not built), run step 8 again at least every 5 sessions. That
   interval is guidance: selection only refuses statistics not dated before the decision [EligibilityFilters.cs:69-78].

## 12. Read the verdicts
All read only; exit 0 whatever they say.
- `xd paper phase-check [--through YYYY-MM-DD] [--epoch ID]` [PaperPhaseCheckCommand.cs:15]
- `xd paper sequential [--scope system|strategy] [--strategy ID]` [PaperSequentialCommand.cs:14]
- `xd paper shadow summary` / `xd paper shadow verify` [PaperShadowCommand.cs:14-16]
- `xd learn phase-check` · `xd plan phase-check [--through YYYY-MM-DD]` · `xd auto check` [XdCommand.cs:147,212,202]
- `xd readiness [STRATEGY] [--version N]` and the **Readiness** screen `/readiness` [ReadinessCliCommand.cs:11; ReadinessCliOptions.cs:32]

## 13. Optional
- Verify-pack gate: `scripts/verify-pack.sh --gate data/exports/verify/<pack-id>` (draw the pack first with
  `scripts/verify-pack.sh --strategies … --basket … --from … --to … --seed … --gross`), then record the result as the
  owner (`POST /api/confidence/pack-gate-results`, password) [scripts/verify-pack.sh:4,9; ReadinessEndpoints.cs:41-43].
  The UI for recording it is **UNVERIFIED** (no frontend caller found).
- Depth and tick recorders: `XD_DEPTH_ENABLED=true` / `XD_TICKS_ENABLED=true deploy/xd-up.sh` [compose.yaml:237,239].
- Assistant eval baseline: `scripts/xd-eval.sh run`, then `scripts/xd-eval.sh accept RUN [--reason TEXT]` (owner act)
  [scripts/xd-eval.sh:8].
- Options: `xd options phase-check [--cut FAMILY,...] [--json]` [OptionsPhaseCheckOptions.cs:30-34]; the family names
  are listed by the verb's own usage (**UNVERIFIED** here).
