# P3-09d — The automated gate: the project decides the next step of the P3 check

- **Status:** Approved design, text unread by the owner (owner, 2026-10-21: chose the fully automated gate design in chat; the spec text itself has not been read by the owner)
- **Phase / roadmap row:** P3 — Simulator, DSL & first strategies; an addition to P3-09 (parts A, B and the assisted check C are built; this is part D). It changes the exit criterion of P3 (ADR 0046 item 15), recorded in [ADR 0048](../adr/0048-the-automated-gate-replaces-the-owners-chart-check-as-the-p3-exit.md).
- **Design refs:** docs/design/18-roadmap.md (P3 exit criterion, P3-09 rows) · docs/research/p3-hand-check.md (section 12) · [ADR 0046](../adr/0046-the-verification-pack-and-the-20-trade-check.md) · [ADR 0047](../adr/0047-the-oracle-and-the-rule-table-gaps.md) · [spec P3-09c](P3-09c-the-assisted-check.md).

## Goal
`verify-pack --gate <pack-folder>` runs the assisted read, applies fixed written rules to its result and states one status with numbered next actions: BLOCKED_DEFECT, DATA_REPAIR_NEEDED, RERUN_NEEDED, PASS_WITH_DATA_FINDINGS or PASS. The exit of P3 is a passing status plus a green `scripts/check.sh`; the owner's manual chart check and signature are no longer required (the owner's decision of 2026-10-21, "fully automated"). The gate must be honest about its power: it shows the engine follows its rules on an independent reading of the bars; it is not a chart-by-eye check.

## Scope
**In:**
1. The mode `--gate <pack-folder> [--second-source-folder <dir>]` of `verify-pack` and the passthrough in `scripts/verify-pack.sh`. It calls `AssistRunner` unchanged (no copy of its logic), then the spot checks, then the rules, then writes `gate-decision.json` and `gate-decision.md` in the pack folder.
2. `GateRules` (pure), `SpotRules` (pure), `GateText` (files), `GateRunner` (the order), in `XDrishti.Api/Simulate/Gate/`.
3. The documents: roadmap exit criterion and rows, `docs/research/p3-hand-check.md` section 12, ADR 0048 (amends item 15 of ADR 0046), `scripts/build-docs.sh`.

**Out:** running the live verb (it needs the owner's token and database); a third data source; changing the engine, the reader, the comparison or the pack; writing `check-log.csv` or any owner verdict (never); a new strategy round (the gate reads what the reader can read, today ORB; another strategy's rows come out `cannot_read` and block, which is honest: the machine cannot check them).

## Definitions
**D1. Inputs.** The verdicts of the assisted read (per row: the reader on the stored bars, the reader on the second source, the bar comparison, the engine's values), the `spots` of `pack.json`, and the stored bars of the spot days.

**D2. The rules** (each category is computed from the rows themselves, not from the row's verdict, so that none is hidden by the precedence of the verdicts; all counts are reported):

| Status (precedence, first match) | Condition | Exit | Next actions (numbered in the files) |
|---|---|---|---|
| BLOCKED_DEFECT | any row where the reader on the STORED bars differs from the engine (`reader_differs`) or reads no trade or the engine row is absent (`cannot_read`); or a spot check is contradicted; or the pack has no trade row | 12 | failing test from the minimal repro of each row first; decide from the rule tables which side departs, fix it; a change voids the pack, draw a new seed; re-run the gate |
| DATA_REPAIR_NEEDED | any row with a gross bar problem **other than a hole**: a price level not constant or outside the band, a gap above 1 % of price | 11 | run the gap backfill only for days inside `Data:Gaps:LookbackSessions` (default 30 sessions, at most 250; it cannot reach older days, which have no repair command yet); refetch a bad second-source day; re-run the gate |
| RERUN_NEEDED | any row with no second source (`source_unavailable`) | 10 | reconnect or fill the folder; re-run the gate |
| PASS_WITH_DATA_FINDINGS | no defect, no repair, no rerun, but a data finding: a row where only the reader on the second source differs (`data_sensitive`), or a **hole** (a cited minute missing on the stored side or the second source), listed with instrument, day, the minutes, the side and whether the hole lies between the trade's entry and exit minutes | 0 | record the listed rows, fields, gap sizes and holes as open data-quality findings, not P3 blockers; a hole older than the backfill lookback stays open for a later repair command |
| PASS | none of the above | 0 | P3 exit met together with a green `scripts/check.sh` |

The thresholds are named constants: `BarComparison.GrossGapPercent` 1.0 and the band and tolerances of the comparison (ADR 0046 Amendment 1), `ReplayOptions` defaults 0.90 (session completeness) and 0.20 (discontinuity) for the spot checks, and `GateRunner.LookbackDays` 7. The rationale is in ADR 0048.

**D3. The spot lines, machine-checked** (from the stored bars and the trades of the pack; what is not checked is stated, never passed silently):
- *excluded_session*: (all) no trade of the pack is dated on the instrument and day, else contradicted. `short_session`: the stored bars inside 09:15 to 15:29 are fewer than 90 % of 375 (that is, below 337.5), else contradicted. `special_session`: confirmed for a Saturday or Sunday; on a weekday the trading calendar (database) decides, so not machine-checkable. `missing_session`: confirmed when a weekday has no stored bar, contradicted when bars exist; a weekend date is not machine-checkable; that a weekday is a trading day is not checked.
- *flagged_day*: confirmed when the first open is more than 20 % from the last close of the nearest earlier day with bars (within 7 days) on the stored bars; otherwise not machine-checkable, because the replay tests adjusted prices (corporate actions) that are not read here.
- *open_at_end*: not machine-checkable (a fact of the run record, which is not in the pack folder).
A "confirmed" flagged day means only that the raw stored jump exceeds 20 %: the replay judges adjusted prices, so it is not a check of the replay's own flag (the decision file says so). A pack with an empty `spots` array prints "spot lines in the pack: 0" and counts as one not-machine-checkable line, never a silent pass. A contradiction blocks; not-machine-checkable lines are listed and counted, change no status, and remain for the optional owner chart check.

**D5. Direction not covered.** The pack holds only trades the engine made, so "the engine skipped a day the reader would trade" cannot appear as a row and the gate cannot see it; that direction is covered by the oracle of P3-08 (ADR 0047). The decision file says so.

**D6. The pack must be complete (BLOCKED_DEFECT, Seq 0 findings).** `pack.json` must hash to its entry in `manifest.json`; its `pack_key` and `count` must equal the manifest's; its rows and spot lines must equal the manifest's `pack.row=` and `pack.spot=` lines; and the trades of `engine-values.csv` must equal the rows of `pack.json` in both directions. A truncated or edited pack therefore cannot pass.

**D4. Files.** `gate-decision.json` (format `xd-gate-1`, rules `gate-1`, status, exit code, the honest limit, SHA-256 of `pack.json`, `engine-values.csv`, `assist-log.csv`, `second-source-bars.csv` and `manifest.json`, counts, findings with repro, spots, numbered next actions) and `gate-decision.md` (the same, readable). No clock: same inputs, same bytes. `check-log.csv` is never read or written; `verdict` is never written.

## Acceptance criteria
1. `--gate` is parsed (alone with `--second-source-folder`, not with `--assist`), in the usage text and in `scripts/verify-pack.sh` (also for the Keychain key rule).
2. `AssistRunner` is reused; its behaviour and files are unchanged.
3. `GateRules` gives each of the five statuses with the exit codes above; BLOCKED_DEFECT beats every other category; a row with a defect and a gross gap counts in both; all counts are reported.
4. `SpotRules` implements D3, with a test for each confirmed, contradicted and not-machine-checkable case; an empty `spots` array is reported (D3).
4b. D6: a truncated pack, an edited `pack.json`, engine trades not in the pack and a pack row not in the engine values each give BLOCKED_DEFECT.
5. Through the runner on a pack fixture (in memory): PASS, PASS_WITH_DATA_FINDINGS, RERUN_NEEDED, DATA_REPAIR_NEEDED and BLOCKED_DEFECT arise from planted inputs, with the right exit code.
6. Determinism: two runs on equal inputs write byte-identical gate files; the hashes in the JSON are those of the input files.
7. `check-log.csv` is byte-identical; on disk with a folder second source the folder holds exactly the expected files.
8. ADR 0048, the roadmap, the protocol and `scripts/build-docs.sh` updated; `scripts/check.sh` green.

## Contract
- **API:** none. **Data:** two files in the pack folder; no database write, no migration. **UI:** none. **Events:** one log line per run, event id 4424 (status and source name).

## Non-functional
No network beyond the assisted read's own (the Dhan source or the folder); no secret read by the gate; one look-back read of at most 7 stored days per flagged spot line. Deterministic output.

## Test plan
Unit: `GateRulesTests` (every status, precedence, independent counts), `SpotRulesTests` (every kind and boundary). Integration (in-memory and on-disk pack fixtures through `GateRunner`): `GateRunnerTests`. Options: `VerifyPackOptionsTests`. Architecture: the existing tests keep the layers; the gate does not reference the engine. Manual: the owner's run on `p3-check-1-ea89b084629b` (needs the stack and the token; not run here).

## Open questions
None. The owner chose the fully automated design. Residual risk, stated in ADR 0048: a misreading of the rule tables shared by the engine, the oracle and the reader is invisible to the gate.

## Done when
- [x] The owner chose the design (2026-10-21)
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] No migration; no API change
- [ ] ADR 0048, roadmap and protocol updated; `scripts/build-docs.sh` run
