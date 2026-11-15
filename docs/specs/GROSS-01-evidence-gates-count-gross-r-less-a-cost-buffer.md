# GROSS-01 — Evidence gates count gross R less a cost buffer

- **Status:** Approved (owner, 2026-11-15, in chat: open decision 11 answered "Count gross R"; the buffer and the per-gate details as the coordinator recommends, under the owner's standing "use my recommendation" rule)
- **Phase / roadmap row:** cleanup, after DROP-OWNER-INPUTS (roadmap open decision 11)
- **Decisions:** ADR 0294 (to be written at build)
- **Design refs:** ADR 0292 (gross forever, its "Open" section), docs/design/08-exits-and-risk.md (costs), 09-learning-engine.md (kill rules), 10-trade-selection.md (readiness), 15-smart-insights-and-automation.md (Auto); CLAUDE.md approvals rule

## Goal
With costs gross forever, five evidence gates could never pass. They now judge **gross R less a stated cost buffer per trade**, so readiness, kills, Auto and live readiness can work again without any owner input, while staying conservative: gross figures flatter a strategy, the buffer takes that back.

## Facts (main at 36d25288)
- Gate 1, forward edge: `ShadowCostBasis` marks every series `Gross` (no confirmed sheet can exist) and `ForwardEdgeReader.cs:62-65` then nulls mean, SD and positive-month share, although the gross series is already computed (`EdgeSeries.Figures`, `GrossOf(row)`). `ForwardEvidenceReader.cs:61-75` also refuses a gross decay reference. Result: readiness Effect, Agreement, Stability and scorecard row 15 read `unavailable`, a hard fail.
- Gate 2, Lab cost-sheet row: `LabEvidenceReader.CostSheetAsync` (`:221-230`) is `Fault`/`Unavailable` without a confirmed sheet → readiness hard fail `CostSheetName`.
- Gate 3, decay cost-stress kill: `GetDecayReportHandler.cs:204-207` is `NotEvaluated` when gross; `KillRules.CostStress` needs net R and a per-trade cost.
- Gate 4, Auto-on precondition `charge-sheet` (`AutoSwitchService.cs:186-187`): always `Fail`, so Auto can never be turned on.
- Gate 5, live-readiness soft item `charge_sheet_confirmed` (`LiveReadinessSource.cs:47-55`, `LiveReadiness.cs:60,75`): always failing, needs a typed reason.

## The cost buffer (researched default)
- Setting `Confidence:Costs:BufferR`, **default 0.10 R per round trip**, range 0.05–0.50, **tighten-only**, declared safer higher. *Corrected at build (coordinator, 2026-11-15):* it is not on the learned allow-list, so the app has no path to change it; changing it is an owner act through host configuration only (appsettings or environment, validated at start, an out-of-range value stops the host).
- Why 0.10 R: an NSE intraday equity round trip at Dhan costs about 0.04–0.06 % of turnover (brokerage capped at ₹20 an order, STT 0.025 % on the sell, exchange about 0.003 %, SEBI fee, stamp duty 0.003 % on the buy, GST on brokerage and exchange fees), plus slippage. With the strategies' stops at about 0.5–1 % of price, that is about 0.05–0.12 R. 0.10 R sits at the top of that range, so the gates stay on the safe side. Swing trades (with delivery STT at 0.1 % each side) cost more per round trip but have wider stops; the same buffer is used, with a note in the ADR.
- Every figure built this way carries the basis label `gross_less_buffer(0.10R)` with the setting snapshot; nothing is ever labelled net.

## Per gate
1. **Forward edge:** each trade's R = gross R − buffer. Mean, SD and positive-month share are computed from it with the existing anchors (`FullEffectR` 0.15, `PositiveMonthShare` 0.70, `MonthMinTrades` 5, `LowerBoundZ` 1.645), unchanged. The decay reference band accepts the same basis (`gross_less_buffer`) and still refuses a reference on a different basis or buffer. Fewer than 2 trades stays unavailable.
2. **Lab cost-sheet row:** becomes the **cost-basis row**: `Clear` when the Lab verdict's figures were judged gross less the buffer (the cost stress below ran), `Unavailable` when the cost stress could not run. Unavailable still never counts as a pass. The hard-fail term is renamed "cost basis not stated or cost stress not run".
3. **Decay cost-stress kill:** net = gross R − buffer, cost = buffer, then the existing `KillRules.CostStress` with `Confidence:Kill:StressMultiplier` (default 1.5, tighten-only) over the trailing 100 trades. Fires when gross − multiplier × buffer < 0 on the trailing mean.
4. **Auto-on precondition:** `charge-sheet` is replaced by `cost-basis`: Pass when the buffer setting is readable and valid; Fail (named) when it is not. An unreadable setting is never a pass.
5. **Live-readiness soft item:** `charge_sheet_confirmed` is replaced by `cost_basis_stated` (soft): passes when the buffer setting is readable and valid. Live trading itself still needs every hard item and the owner's act; no setting places an order.

## Scope
**In:** the five gates, the setting (catalog, tier, tighten-only rule), labels, ADR 0294, docs 08/09/10/15, tests.
**Out:** removing `CostChoice` and the now-dead net code (a later cleanup slice, listed in ADR 0294); any change to thresholds other than the buffer; any order placement.

## Acceptance criteria
1. A forward series of n ≥ 2 gross trades gives mean, SD and month share computed on gross − buffer; a fixture with gross mean 0.20 R and buffer 0.10 R gives mean 0.10 R; with buffer 0.25 R the effect is negative.
2. Readiness no longer hard-fails only because costs are gross: a fixture that passes every other input passes Effect, Agreement, Stability and row 15.
3. The decay reference band compares only like with like (same basis and buffer); a different buffer is refused with a named reason.
4. The cost-stress kill fires on a trailing series whose gross mean is below multiplier × buffer and not above it (boundary tests at 1.5 and 3).
5. The Lab cost-basis row is Clear with the stress run, Unavailable without it; Unavailable is a hard fail.
6. Auto can be turned on when every other precondition passes and the buffer setting is valid; an unreadable setting refuses it, named.
7. Live readiness's soft item passes with a valid setting and fails (needing a typed reason) when unreadable.
8. *(Corrected at build.)* The catalog declares the buffer Advanced and safer higher; an approved learned change to it is never laid over the configuration (not on the allow-list), so the gates keep the configured value; a configured value out of range stops the host (settings catalog and configuration tests).
9. Every figure and report shows the basis `gross_less_buffer(<value>R)`; no figure is labelled net.
10. `ExecutionBoundaryArchitectureTests` stays green; no new order path.

## Test plan
Unit: ForwardEdgeReader, ForwardEvidenceReader band, KillRules.CostStress with the buffer, LabEvidenceReader, AutoSwitchService, LiveReadiness. Update the pinned refusal tests (ScorecardReaderTests:757, GetDecayReportHandlerTests:236, AutoSwitchServiceTests:226, RampTests:100) to the new behaviour. Integration: settings catalog (tier, tighten-only), readiness/auto endpoints. Architecture: unchanged rules.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated if the API changed
- [ ] ADR 0294 written; design docs and roadmap decision 11 updated
