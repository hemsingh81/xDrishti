# 0245 — The unused-code removal list from the AU-05 audit

- **Status:** Proposed (owner to accept the list before AU-06/AU-07 delete anything).
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Applies [0241](0241-code-is-removed-only-from-a-recorded-list-of-unreachable-code-no-spec-plans-to-use.md); spec AU-05; feeds AU-06 and AU-07.

## Context
AU-05 audited `backend/src`, `backend/tests` and `frontend/src` for code nothing reaches. Method (read-only, no tool added): every
`public`/`internal` type and method name in `backend/src` was counted across all `.cs` files (migrations and spikes excluded from
the count of callers, included when checking helpers they call); frontend files by import graph and exports by name count; the
`appsettings.json` keys by name. Each hit was then grepped in `docs/specs`, `docs/design` and the tests. Names that are only
reached by reflection or DI (EF `*Configuration` classes, registration extension classes, log holders, CLI/MCP entry points,
`ToCode`-style extension methods) were reviewed and are **not** candidates. Everything in `XDrishti.Execution` (P12), the P13
recorders, P14 archive, migrations and the ADR 0241 null seams is excluded.

## Candidates

### Certain (no reference anywhere, no spec or design plans it)
| # | Item | Evidence | Slice |
|---|------|----------|-------|
| 1 | `BarGrid.LastOnDate` (`Domain/Options/Remark/BarGrid.cs`) | 0 callers, 0 tests, 0 spec/design hits | AU-06 group A |
| 2 | `EodErrors.FetchFailed` and `FetchFailedCode` (`Application/Market/Eod/EodErrors.cs`) | 0 callers, 0 tests; the live code is `DataErrors.RepairFetchFailedCode` | AU-06 group A |
| 3 | `daysBetween` (`frontend/src/features/live/lib/dates.ts`) | 0 callers, 0 tests, no spec | AU-07 group A |
| 4 | `needsPassword` (`frontend/src/features/learning/lib/mlText.ts`) | 0 callers, 0 tests | AU-07 group A |
| 5 | `isReplayTimeframe` (`frontend/src/features/reports/lib/replayModel.ts`) | 0 callers, 0 tests | AU-07 group A |
| 6 | `MAX_PIVOT_CELLS` (`frontend/src/features/reports/lib/pivotLimits.ts`) | 0 readers, 0 tests (delete the file if it holds nothing else) | AU-07 group A |

Risk of each group: low (compile-time proof; the compiler and `tsc` fail on a wrong removal). Together under half a day.

### Doubtful (left in place; the owner decides)
| Item | Why doubtful |
|------|--------------|
| `PeadV1.VariantOf` | 0 callers and 0 tests, but it is the label protocol of a built-in strategy; may be meant for the lab ledger. |
| `LlmLog.KeyMissing`, `DataLog.EodCrashed` | Log messages never called. They look like a missing diagnostic (see Findings), so the fix may be to call them, not delete them. |
| Test-only methods (reachable only from tests): `RollingRequest.IsFailedAttempt`, `ReplayPlan.BaselineRequest`, `DataManifest.IsForbiddenColumnName`, `LabRunStates.HoldsKey/IsCancellable/IsTerminal`, `SurpriseMath.Ttm`, `RegimeCellRule.ShrinksToParent`, `CheckStatus.IsKnownResult`, `PairVariant.FromLabel`, `EngleGranger.TestPrices`, `ServiceRun.LinkDataJob`, `Transaction.Supersede`, `ChargeSheetAct.IsAboutConfirmation`, `ChargeSchedule.RatesOf`, `FeatureFrame.HasTimeframe/SpecOf`, `RandomEntryWalker.RandomTrades`, `HashingFileSink.GetMemory`, `ModelHoldOutLooks.ClaimAsync` (22 items) | A test is the only caller; removing them removes the pinned behaviour with them. Several are guards for a rule (hold-out, supersede). |
| `plannedNavItems`, `isJobRunning` (frontend) | Used by tests only. |
| Frontend exported types and test kits with no other reader (about 60 `type` exports, `testing.ts*` helpers) | No runtime effect; helpers are used by tests. Not worth a slice. |
| `features/lab/monaco-api.d.ts` | An ambient module declaration; looks unimported but TypeScript loads it implicitly. |
| `MigrationBuilderExtensions` helpers, `DesignTimeDbContextFactory`, EF configurations, `*Registration`, `*Log` holders | Reached by migrations, tooling or reflection. Protected. |

### Protected (excluded by ADR 0241)
`XDrishti.Execution` including `FakeBroker.LagTradeBook` (P12-06), `ClosedProtectionSource`, `IStoredTrades`, in-memory stores; settings keys (all 12 leaf keys in `appsettings.json` are read).

### Trading-safety code
None is proposed. Any halt, freeze, gate or reconciliation item needs the owner's explicit yes (ADR 0241 rule 4).

## Removal slices
- AU-06 group A: items 1 and 2 (backend, a few lines, risk low).
- AU-07 group A: items 3 to 6 (frontend, risk low).
Doubtful items are not scheduled.

## Findings that look like defects, not dead code
- `LlmLog.KeyMissing` ("model server is enabled but the API key is empty") is never called, so that misconfiguration is silent.
- `DataLog.EodCrashed` ("the scheduled end-of-day update could not be started") is never called, so that failure may not be logged.

## Consequences
The certain list is small (6 items) because the codebase was audited slice by slice and most parked code is protected. Nothing is deleted by this ADR.
