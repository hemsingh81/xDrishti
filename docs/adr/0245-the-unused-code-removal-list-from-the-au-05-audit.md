# 0245 — The unused-code removal list from the AU-05 audit

- **Status:** Accepted by delegation (owner instruction in chat 2026-11-09: audit first, record the list in an ADR, then remove in small reviewed slices). All 11 certain items removed in AU-06/AU-07 after an independent review; the owner may still review this list.
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Applies [0241](0241-code-is-removed-only-from-a-recorded-list-of-unreachable-code-no-spec-plans-to-use.md); spec AU-05; feeds AU-06 and AU-07.

## Context
AU-05 audited `backend/src`, `backend/tests` and `frontend/src` for code nothing reaches. The ADR itself deleted nothing; AU-06 and AU-07 did, item by item.

## Method and its limits
1. **Compiler/analyser pass (backend).** `dotnet build XDrishti.slnx` with `EnforceCodeStyleInBuild=true` and IDE0051 (unused private member), IDE0052 (private member never read), IDE0060 (unused parameter) and CA1822 raised to warning through a temporary `.editorconfig` change that was reverted (no committed setting changed, no tool added). Result: 7 IDE0051 (3 are EF private constructors, a false positive), 4 IDE0052, 15 IDE0060 (mostly interface or delegate shaped parameters, not candidates), 0 CA1822. These analysers only see private members; they say nothing about public or internal ones.
2. **Name-count scan (backend).** Every public/internal type and method name in `backend/src` (spikes and migrations excluded) was counted across all `.cs` files. A name that appears once in `src` is a candidate; one that appears in tests only is "test-only". This is a text match, not a compiler proof: it misses unused members that share a name with a used one (overloads, common names such as `ToCode`), and it does not sweep properties, fields, constants or enum members.
3. **Frontend.** `tsc -p tsconfig.app.json --noEmit --noUnusedLocals --noUnusedParameters` reports 0 errors (it only sees non-exported symbols). Exports and files were checked by an import-graph and name-count script. `knip` is not installed and was not added.
4. **Routes.** All 355 paths of the generated OpenAPI schema were compared with the frontend sources, CLI, MCP, deploy and scripts. 342 have a consumer. 13 have none outside tests and docs (listed under Doubtful). CLI verbs (`Api/Cli`) and MCP tools were not individually swept for reachability: they are owner-only entry points (ADR 0241 rule 3) and the `[McpServerTool]` attribute is not used, so tools are registered by code, not found by a text match.
5. **Settings.** All leaf keys of the five host files (`Api` 12, `Execution` 5, `Feed` 12, `Mcp` 10, `Worker` 6) were matched with a name in `backend/src`; every key is read. Logging/Serilog keys excluded.
6. **Plan check.** Each candidate was searched in `docs/specs` and `docs/design`.
Names reached only by reflection or DI (EF `*Configuration`, registration extensions, log holders, entry points, migration helpers) were reviewed and are not candidates. `XDrishti.Execution` (P12), P13 recorders, P14 archive, migrations and the ADR 0241 null seams are excluded.

## Candidates

### Certain (nothing reaches them, no spec or design mentions them)
| # | Item | Evidence | Spec/design hits | Slice |
|---|------|----------|------------------|-------|
| 1 | `BarGrid.LastOnDate` (`Domain/Options/Remark/BarGrid.cs`) | name scan: 1 hit, no test | 0 | AU-06 A, done |
| 2 | `EodErrors.FetchFailed` and `FetchFailedCode` (`Application/Market/Eod/EodErrors.cs`) | name scan, no test; the live code is `DataErrors.RepairFetchFailedCode` | 0 | AU-06 A, done |
| 3 | `DataLog.EodCrashed` (`Application/Market/Bars/DataLog.cs`) | name scan: log message with no caller; leftover | 0 | AU-06 A, done |
| 4 | `ProposalAnswerReader.Clip` (private) | IDE0051 | 0 | AU-06 A, done |
| 5 | `ProtocolF.Share(double?)` overload (private, line 290) | IDE0051 | 0 | AU-06 A, done |
| 6 | `AiSqlRunner.Short` (private) | IDE0051 | 0 | AU-06 A, done |
| 7 | `ScheduleCalculator.WeeklyReach` (private field) | IDE0052 | 0 | AU-06 A, done |
| 8 | `LegState._t1Epoch` (private field, written never read) | IDE0052 | 0 | AU-06 A, done |
| 9 | `daysBetween` (`frontend/src/features/live/lib/dates.ts`) | import/name scan, no test | 0 | AU-07 A, done |
| 10 | `isReplayTimeframe` (`features/reports/lib/replayModel.ts`) | same | 0 | AU-07 A, done |
| 11 | `MAX_PIVOT_CELLS` (`features/reports/lib/pivotLimits.ts`; delete the file if it holds nothing else) | same | 0 | AU-07 A, done |

Risk: low. Items 1 to 8 are checked by the compiler on removal (a wrong removal does not build); 9 to 11 by `tsc`. Items 1 to 8 are about half a day, 9 to 11 under an hour. Because the method has the limits above, the AU-06/AU-07 builds and tests are the final proof, and a reviewer samples the list before removal.

### Doubtful (left in place; the owner decides)
- **Kept by decision (owner/coordinator, 2026-11-09):** `needsPassword` (encodes the step-up rule, ADR 0241 rule 4), `ModelHoldOutLooks.ClaimAsync`, `PeadV1.VariantOf`, `plannedNavItems`, `isJobRunning`, `PairVariant.FromLabel`, `DataManifest.IsForbiddenColumnName`, `LlmLog.KeyMissing` (planned in P9-01; now logged at start, see Findings).
- **Test-only (20 names), left in:** `RollingRequest.IsFailedAttempt`, `ReplayPlan.BaselineRequest`, `LabRunStates.HoldsKey`, `LabRunStates.IsCancellable`, `LabRunStates.IsTerminal`, `SurpriseMath.Ttm`, `RegimeCellRule.ShrinksToParent`, `CheckStatus.IsKnownResult`, `EngleGranger.TestPrices`, `ServiceRun.LinkDataJob`, `Transaction.Supersede`, `ChargeSheetAct.IsAboutConfirmation`, `ChargeSchedule.RatesOf`, `FeatureFrame.HasTimeframe`, `FeatureFrame.SpecOf`, `RandomEntryWalker.RandomTrades`, `EvalFraction.ToDouble`, `MigrationBuilderExtensions.GrantColumnUpdateToAppRole`, `GrantTableToAppRole`, `MakeAppendOnlyForAppRole`. A test is the only caller; removal would delete the pinned behaviour with it.
- **Unread private fields of indicators:** `BbMiddle._k`, `MacdLine._signalPeriod` (stored constructor arguments; harmless).
- **`BarAggregates.MinuteOfDay`** (private SQL snippet): the spec/design search finds the word "MinuteOfDay" three times; not checked further.
- **Routes without a UI, CLI or MCP consumer (13):** `/api/assistant/briefings/preview`, `/api/confidence/pack-gate-results`, `/api/learning/counterfactuals`, `/api/paper/capture-runs`, `/api/paper/config/changes`, `/api/paper/entries`, `/api/paper/shadow/settings`, and the P12/P13 ones `/api/execution/alarms`, `/api/execution/reconciliation`, `/api/execution/breaks/{id}/resolve`, `/api/live-trading/halts`, `/api/live-trading/notices`, `/api/live-trading/outcomes`. They are owner or tool endpoints with tests; none is proposed.
- **Unused parameters (15 IDE0060):** shaped by interfaces and delegates; not candidates.
- **Frontend:** `monaco-api.d.ts` (ambient module declaration, loaded implicitly), about 60 exported types and the `testing.ts*` helpers (no runtime effect).

### Protected (ADR 0241)
`XDrishti.Execution` including `FakeBroker.LagTradeBook`, `ClosedProtectionSource`, `IStoredTrades`, in-memory stores; `MigrationBuilderExtensions` helpers used by migrations; EF configurations, registration extensions, log holders.

### Trading-safety code
None is proposed. Any halt, freeze, gate or reconciliation item needs the owner's explicit yes (ADR 0241 rule 4).

## Removal slices
- AU-06 group A: items 1 to 8 (backend, risk low).
- AU-07 group A: items 9 to 11 (frontend, risk low).
Doubtful items are not scheduled.

## Findings that looked like defects
- `LlmLog.KeyMissing` was never called, so an enabled model server with an empty key was silent. Fixed in this slice: `LlmStartupCheck` logs it once at start when `Enabled` and the key is empty (it never calls the server; test `LlmStartupCheckTests`).

## Consequences
The certain list is short (11 items) because most parked code is protected and the name scan cannot prove more. The ADR itself deleted nothing; AU-06 and AU-07 did, item by item.
