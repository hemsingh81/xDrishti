# CF-04d — Source-aware replay of backfilled bars

- **Status:** Approved (coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended). Written 2026-11-12. Not built.
- **Phase / roadmap row:** CF programme, row CF-04d (P1 data / ISS-19); follows [CF-04](CF-04-history-backfill-to-five-years-daily-bhavcopy-and-corporate-actions.md) Part B ("Part B as built" items 6a, 8, 9).
- **Design refs:** docs/design/05-data-and-brokers.md §4.2 · ADR 0017 §5 · [ADR 0270](../adr/0270-the-dhan-one-minute-backfill-writes-only-before-each-instruments-first-csv-bar-through-a-resumable-chunk-ledger-and-pre-seam-bars-stay-out-of-every-run.md) decisions 7 to 10 · [ADR 0271](../adr/0271-the-run-record-states-depth-per-instrument-and-the-survivorship-limit-from-its-own-stored-bars-and-only-for-a-run-that-rests-on-backfilled-history.md) · ADR 0263 (omit when empty)
- **Needs:** CF-04 Part B (built). Independent of CF-04e.
- **Size:** 2.0 agent-days (verification rule, basis and store 0.5; factor mode and the three readers 0.5; key, record, label and `xd data history` 0.3; date-based seam guard and cut 0.2; tests and ADR 0.5). Uncalibrated.
- **Safety: YES.** Money-path data: a wrong factor on a pre-seam bar silently corrupts every simulation, training set and verdict built on it. Everything unknown stays excluded; the admission switch defaults OFF; no fetch, order, account or allow-list change.

## Goal
Let a simulation run use an instrument's backfilled Dhan bars from before its seam, adjusted by the seam rule, but only when that instrument is proven verified. Every other instrument keeps today's behaviour, and so do its bars, hash and key.

## Verified facts (read 2026-11-12, `backend/src`)
1. `SourceAwareFactors.FactorAt` (Domain/Market/History/SourceAwareFactors.cs:28-40) decides by **date** (`barDate < _seam`); `SeamGuard.Split` (SeamGuard.cs:18) and `SeamAwareBars.CutAsync` (Application/Market/History/SeamAwareBars.cs:31-32) cut by **instant**. So a Dhan bar on the seam date before the first CSV minute would pass the writer guard and then take the CSV rule. **This mismatch is unsafe.**
2. The replay adjusts every bar by `AdjustmentFactors.FactorAt` (Simulation/Replay/ReplayBars.cs:39-49). The null-model walker does the same (RandomEntryWalker.cs:196-200), and so do the feature extractor (ITradeFeatureExtractor.cs:18) and the trade replay (Reporting/GetTradeReplayHandler.cs:63-65). For a bar before the seam, the pre-seam rule equals `AdjustmentFactors.FactorAt`. For a bar at or after the seam, the CSV rule equals `AdjustmentFactors.FactorAt(max(barDate, csvAdjustedThrough))`. They differ only where an applied action lies inside the CSV span, and those actions are the ones that trigger `csv_adjusted_twice` (InstrumentInputReader.cs:87-90).
3. The bars hash covers the raw stored bars before adjustment (BarStreamHash.cs:8-9, InstrumentReplayer.cs:142). The key's `i=` line holds the bars hash, the skip, the actions as `ex:kind:num:den`, and the tick (RunAssembler.cs:110-115). Neither the seam nor `csvAdjustedThrough` is in it.
4. `IBackfillSeams.SeamOfAsync` returns `high_water_at` (BackfillStore.cs:154-160). That value is only ever raised (`GREATEST`, :61). `CurrentSeamAsync` is a scan with `ORDER BY ts LIMIT 1` over `source = 'csv'` (:15). `market.backfill_cut` returns the high-water instant (migration AddHistoryBackfill:54-58).
5. Probe statuses are `none | passed | unresolved` (BackfillStatus.cs:12-18; check constraint at migration :47). The probe stores no basis: if actions are added or edited later, a `passed` row stays `passed` (ProbeSeamHandler.cs:177-192).
6. The continuity check runs only when every chunk is finished (RunBackfillHandler.cs:247-249). It saves only when the flag changes (:351-353), so `seam_jump = false` and `jump_pct = null` mean the same thing whether or not the check ever ran. **This is fail-open if it is read as "verified".**
7. When the seam moves earlier, the next backfill run marks the probe `unresolved` (RunBackfillHandler.cs:164-177). The high-water mark never comes down, so a re-probe can never make the recorded seam equal the cut again. The repair job writes `source = 'dhan'` bars into holes inside the CSV span (RepairBarHoles.cs:192). Such a bar dated before an in-span ex-date is raw, and the CSV rule would leave it unadjusted.
8. Day buckets start at India midnight (BarAggregates.cs:25). The daily readers filter with `ts >= market.backfill_cut(...)` (e.g. BarQueries.cs:209). So once an instrument has a seam row, its **first CSV day is dropped** from every daily reader. This is fail-closed but wrong.
9. Every pre-seam bar is excluded today for every instrument. The only marker is a comment (`seam_unverified`, InstrumentInputReader.cs:26); there is no skip reason or label. AU-12 runs through `RunSimulationQuery` (SimAllChunkExecutor.cs:87), so it uses `InstrumentInputReader`. The cards share that reader and refuse on a hash change (BuildTradeCardsHandler.cs:122).

## Decisions (each as recommended)
1. **One admission switch, OFF.** `Data:History:Replay:AdmitVerified = false` lives in the existing `Data:History` section. While it is OFF, every record, key and label equals Part B's byte for byte. *Rejected:* admitting automatically once the code is built.
2. **Verified means all of the following.** If any one fails, the instrument is excluded with the first failing reason, checked in this order:
   - the switch is ON;
   - the probe is `passed` (`not_probed` when the status is `none`, `seam_unresolved` when it is `unresolved`);
   - the stored seam equals both the current seam and the high-water mark (`seam_moved`);
   - the instrument has no `planned` or `failed` chunk (`backfill_unfinished`);
   - the continuity check ran on the current basis (`continuity_unchecked`);
   - the continuity check found no jump (`seam_jump`);
   - the basis has not changed since the probe or the check (`basis_changed`);
   - there is no `dhan` bar between the seam and the last in-span ex-date (`dhan_in_csv_span`).

   The rule is a pure Domain function (`SeamVerification.Decide`). *Rejected:* trusting `probe_status` alone (facts 5, 6, 7).
3. **A change after the probe invalidates verification.** The basis is the SHA-256 of `seam instant | each applied action ex:kind:num:den:onCsv`, where `onCsv` = ex-date ≤ `csvAdjustedThrough`. The probe writes `probe_basis` on a pass and clears it on a fail. The continuity check writes `continuity_basis`, `seam_jump` and `jump_pct` on **every** run. Admission needs both bases to equal the current basis. A CSV load that moves `csvAdjustedThrough` without crossing an ex-date keeps the instrument verified; adding, editing or undoing an action, or a CSV load that crosses an ex-date, means probing again with `xd data probe-seam` and re-checking with the next `xd data backfill`. *Rejected:* comparing `probed_at` with action timestamps (it misses undo and `csvAdjustedThrough` changes).
4. **A seam that moved earlier stays excluded in this slice** (`seam_moved`). The cut stays at the high-water mark, so the newly imported earlier CSV bars stay out as well. Dhan rows may sit between the new seam and the old one, and `bars_1m` has no delete grant. Repairing this is a later slice.
5. **Factor mode, not new signatures.** `AdjustmentFactors` gets a source-aware mode, `AdjustmentFactors.SourceAware(seamDate, csvAdjustedThrough)`. Its `FactorAt` follows `SourceAwareFactors` (fact 2), and its `Actions` and `ActionsAfter` are unchanged. The replay, the walker, the extractor and the trade replay all take it as they are. `SourceAwareFactors` stays the oracle (a property test checks that both give equal factors on every date).
6. **Three readers admit; every other reader stays cut.** One shared read, `SeamAwareBars.ReadForRunAsync`, returns either the raw bars from `SimulationBounds.From` with the source-aware factors (admitted) or today's cut bars with the plain factors. It is used by `InstrumentInputReader` (runs, cards, AU-12, Lab), by `ExtractTradeFeaturesHandler` (so training sees the bars the run saw) and by `GetTradeReplayHandler`. The SR-01 replay (`ClampedBars`), pairs, option re-mark and condors, planning, the assisted-check days and **every daily SQL reader** keep the high-water cut. So daily-table inputs (turnover, tier, relative strength, context, breadth) have no pre-seam rows, and a pre-seam entry that needs one is blocked by the existing rules (fail closed). Daily and weekly bars **inside** the replay are built from the adjusted minutes, so they are correct.
7. **An admitted instrument does not raise `csv_adjusted_twice`.** Under the CSV rule no in-span action touches a CSV bar. A probe pass shows the CSV is on the "adjusted through" basis: in the opposite case (CSV raw for that action) the probe fails, because Dhan's bar gets the factor and the CSV bar does not. Every other instrument keeps the guard and the `ack` flag as they are.
8. **Pure-source days.** The writer guard drops every Dhan bar dated **on or after the seam date** (`beyond_seam`). `market.backfill_cut` and `SeamAwareBars` cut at India midnight of the high-water seam date. This fixes facts 1 and 8. Minutes on the seam date before its first CSV minute can only be repair bars, and those are treated like every other repair bar in the CSV span.
9. **The key changes only where bars are admitted.** For each admitted instrument, in run order, after the `i=` lines, the key gets `seam=<id>|<seam date>|<basis sha256>`. The instrument record gets an optional `preSeam {seamDate, basisSha256}`, omitted otherwise. No schema version is bumped (ADR 0263 pattern). The bars hash of an admitted instrument covers its pre-seam raw bars, so it changes by design. Old runs keep their keys.
10. **Honesty surfaces.** When the switch is ON and an instrument of the run has a seam row, the run gets a label line (not keyed): `backfill: pre-seam bars admitted for A instruments (source-aware); excluded for E (reason n, ...)`. `xd data history` gains a `verified` column with the reason. Part C's `depth:` lines then appear by themselves.

## Scope
**In:** decisions 1-10; migration `AddSeamVerificationBasis` (columns `probe_basis char(64) NULL` and `continuity_basis char(64) NULL` on `market.backfill_seams`, and `CREATE OR REPLACE` of `market.backfill_cut`; Down restores both); `IBackfillSeams` gains an admission read (record, current seam, unfinished chunks, `dhan` in span); ADR (next free number); doc 05 §4.2; the "not met" lines of CF-04 and ADR 0270.
**Out:** the API and card (CF-04e); daily readers using pre-seam days; the SR-01 replay and the other cut readers listed in decision 6; repairing a moved seam; confirming a `seam_jump` (the owner adds the missing action instead); switching on the fetch (an owner act).

## Acceptance criteria
Hand instrument **X** (CF-04 AC 8): split 1:5 (factor 0.2) ex 2023-03-10 and bonus 1:1 (0.5) ex 2024-08-01, both applied; seam 2024-04-02 09:15 IST; `csvAdjustedThrough` 2026-04-30; probe passed; every chunk done; continuity checked; both bases current; switch ON.
1. **Admitted bars equal the hand case.** Dhan 2022-12-01 close 2,500.00 and volume 1,000 are fed as **250.00** and **10,000**. Dhan 2024-04-01 15:29 close 500.00 is fed as **250.00** (bonus only; the split is past). CSV 2024-04-02 09:15 open 252.10 stays **252.10** (continuity +0.84 %). CSV 2024-05-02 close 260.00 stays **260.00**, where the plain factors would give 130.00. The run of X is not skipped `csv_adjusted_twice`, with or without `ack`. A fixture BUY at the 2022-12-01 09:16 close (raw 2,500.00) enters at **250.00**. *Replay test plus a property test (mode == `SourceAwareFactors` on every date 2021-01-01..2026-12-31).*
2. **Unverified instruments stay excluded, each with its reason.** In the same run: **Y** has probe `none` (`not_probed`); **Z** has last pre-seam close 250.00 against first CSV open 330.00, +32 % (`seam_jump`); **W** has Dhan at 0.2× the CSV with no action applied, median 8,000 bps (`seam_unresolved`, seeded pre-seam rows). The fed bars of Y, Z and W start at their seams, and their hashes and `i=` lines equal Part B's. The label reads `admitted for 1 ... excluded for 3 (not_probed 1, seam_jump 1, seam_unresolved 1)`. *Run test on a real TimescaleDB.*
3. **A moved-earlier seam is excluded.** If X gets a CSV import starting 2024-03-01, X reads `seam_moved`. Its cut stays at 2024-04-02 00:00 IST (the new March CSV bars stay out as well), and a passing re-probe does not admit it. *Unit (decide) plus integration.*
4. **A change after the probe invalidates verification.** If a split 1:2 ex 2026-06-15 is added after the probe, X reads `basis_changed` and runs as in Part B. After a re-probe and the next continuity check it is admitted at **125.00** (Dhan 2022-12-01) and **130.00** (CSV 2024-05-02), with volume ×20. If `csvAdjustedThrough` moves 2026-04-30 → 2026-05-29 (no ex-date crossed), X stays verified. If it moves to 2026-06-30 (crossing 2026-06-15), X reads `basis_changed`. *Unit.*
5. **Every other way of being unverified fails closed.** A `planned` or `failed` chunk gives `backfill_unfinished`. A continuity check that never ran (`continuity_basis` null; `seam_jump = false` is not enough) gives `continuity_unchecked`. A repair `dhan` bar on 2024-06-03 10:00 (between the seam and ex 2024-08-01) gives `dhan_in_csv_span`. The switch OFF gives every record, key and label byte-equal to Part B's goldens. *Unit plus golden.*
6. **Hash and key change by design.** X's bars hash covers its raw bars from its first Dhan bar and differs from Part B's. The key gains exactly `seam=<X>|2024-04-02|<basis>`. A run with no admitted instrument has a key byte-equal to Part B's. Two identical runs give identical keys. *Assembler golden.*
7. **Readers agree.** The trade cards of an admitted run reproduce its trades. If admission is revoked between the run and the cards, the cards refuse with `simulation.cards_stale`. Feature extraction and the trade replay of a pre-seam trade read the same fed bars as the run. *Handler tests.*
8. **Pure-source days.** A Dhan answer with bars on 2024-04-01 and 2024-04-02 stores only those of 2024-04-01; the 2024-04-02 bars count as `beyond_seam`. With a seam row present, the 2024-04-02 day bucket is returned by `ReadDailyBarsAsync` (today it is dropped). *Integration.*
9. Migration with Down; grants unchanged; `HistoryBackfillArchitectureTests` updated (the new read stays inside `SeamAwareBars`, so the raw-reader list is unchanged); no `gen-api` change; `scripts/check.sh` green.

## Contract
- **Settings:** `Data:History:Replay:AdmitVerified` (bool, default false).
- **Data:** migration `AddSeamVerificationBasis` (see Scope).
- **API:** none. **Verbs:** `xd data history` gains `verified` and its reason.

## Test plan
Domain: `SeamVerification.Decide` (each reason and the order of precedence), the basis hash, the factor-mode property, and the date-based `SeamGuard`. Simulation: `ReplayBars` and the walker with the mode (AC 1). Application: the shared read, the run, the cards, extraction and the trade replay (AC 2-7). Integration on a real TimescaleDB: AC 2, 3, 8 and the timing of the two scans. Architecture: raw readers, cut readers. Goldens: Part B keys and records with the switch OFF.

## Risks
1. **Scan cost:** each candidate instrument per run gets one scan for the current seam and one for `dhan` in the span, each over up to about 270,000 rows. Budget: 100 ms per instrument (measured). Over budget, add a partial index on `(instrument_id, ts) WHERE source = 'csv'` and update `DataTableGrantsTests`.
2. **Flipping the switch during a simulate-everything job** mixes admitted and cut chunks in one job. Each key stays honest, but flip it only between jobs.
3. **Basis churn:** an action edit forces a re-probe (Dhan requests). This is accepted as the price of fail closed.

## Shortcuts
- The continuity re-check runs only inside `xd data backfill`, which needs Dhan connected even with nothing to fetch. There is no standalone verb.
- Per-instrument exclusion reasons are in the label counts and `xd data history`, not in `run.json`.
- A moved-earlier seam and a `seam_jump` have no owner "confirm" path here.

## Done when
- [ ] AC 1-9 have evidence; `scripts/check.sh` green; reviewer (Opus): no blockers or majors
- [ ] Migration added; ADR written; doc 05 §4.2, the CF-04 spec and ADR 0270 "not met" lines updated
- [ ] Owner act stated: run `probe-seam`, then `backfill`, then set `AdmitVerified`
