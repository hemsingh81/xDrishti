using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>What the one plain pass over the hold-out produced: the trades of the chosen exit of each known cell and their mean net R.</summary>
internal sealed record HoldOutFigures(int Trades, double? MeanR, DateOnly From, DateOnly To);

/// <summary>The one plain pass over the hold-out (an interface so the unlock can be tested without an engine).</summary>
internal interface IHoldOutPass
{
    ValueTask<Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken);
}

/// <summary>
/// The only reader of the sessions inside the locked hold-out (spec CF-06, D5): one plain pass of the frozen plan over the block. For each timeframe the candidate exits are walked over the block, and for each cell the development full test knew
/// (timeframe and side) the trades of the exit that test chose are taken; a cell the development did not know is not in the plan and is left out. Used by the unlock only; an architecture test pins that.
/// </summary>
internal sealed class HoldOutPass(
    IQueryHandler<RunComparisonQuery, Result<ComparisonRun>> comparisons,
    IStrategyStore strategies,
    IStrategyChecker checker,
    ICostSheetStore sheets,
    LabSettings settings,
    LabPreregistration preregistration,
    IPlanFreezeStore freezes) : IHoldOutPass
{
    public async ValueTask<Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(block);
        var version = await strategies.GetByHashAsync(run.StrategyHash, cancellationToken);
        var description = version is null ? null : checker.Check(version.SourceText).Description;
        if (description is null)
        {
            return Result.Failure<HoldOutFigures>(LabErrors.HoldOutUnavailable("The stored strategy text no longer compiles, so its plan cannot be run on the hold-out."));
        }

        // the frozen plan: the plan about to run, from the inputs of this run as they stand now, must be the one the version froze first (D2); no freeze, or a different plan, is not the frozen plan and is not run on the hold-out
        if (await freezes.FirstAsync(run.StrategyId, run.Version, cancellationToken) is not { } frozen)
        {
            return Result.Failure<HoldOutFigures>(LabErrors.HoldOutUnavailable("This run has no frozen plan; post a full run (same explicit window, or a new run) and unlock that run."));
        }

        if (frozen != await preregistration.PlanHashAsync(run, version!, cancellationToken))
        {
            return Result.Failure<HoldOutFigures>(LabErrors.HoldOutUnavailable("The plan of this run differs from the plan the version froze first (a re-plan), so it is not the frozen plan and is not run on the hold-out."));
        }

        var sheet = await sheets.FindAsync(settings.CostSheetId, cancellationToken);
        if (sheet.IsFailure || sheet.Value.Schedules[0].EffectiveFrom > block.Start)
        {
            return Result.Failure<HoldOutFigures>(LabErrors.HoldOutUnavailable("The hold-out is costed: the charge sheet is not stored or not in force on its first day."));
        }

        var errors = new List<string>();
        var exits = ExitFlags.Parse([.. settings.Exits], null, errors);
        if (exits is null || exits.Candidates.Count < 2)
        {
            return Result.Failure<HoldOutFigures>(LabErrors.HoldOutUnavailable("The candidate exits are not usable: " + string.Join("; ", errors)));
        }

        var rules = FillRules.Default;
        var fills = new FillRulesInput(rules.EntryLimitPercent, rules.Slippage.Ticks, rules.Slippage.BarRangeFrac, rules.Slippage.StopExitExtraTicks);
        var timeframes = description.Timeframes.Count == 0 ? [Timeframe.M15] : description.Timeframes;
        var r = new List<double>();
        foreach (var timeframe in timeframes)
        {
            var request = new SimulationRequest(run.StrategyId, new Dictionary<string, double>(), timeframe.ToCode(), block.Start, block.HistoryLast, [.. run.Universe.Instruments.Select(i => i.Id)], null, CostChoice.Sheet(sheet.Value.Id), false, null, fills, null, null, run.StrategyHash) with { Exits = exits };
            var compared = await comparisons.HandleAsync(new RunComparisonQuery(request), cancellationToken);
            if (compared.IsFailure)
            {
                return Result.Failure<HoldOutFigures>(compared.Error);
            }

            foreach (var side in compared.Value.Candidates[0].Run.Trades.Select(t => t.Trade.Side).Distinct())
            {
                var sideText = side.ToString().ToUpperInvariant();
                var cell = verdict.Cells.FirstOrDefault(c => c.Timeframe == timeframe.ToCode() && c.Side == sideText);
                if (cell is null)
                {
                    continue;
                }

                var chosen = compared.Value.Candidates.FirstOrDefault(c => c.Id == cell.Chosen);
                if (chosen is null)
                {
                    return Result.Failure<HoldOutFigures>(LabErrors.HoldOutUnavailable($"The exit the development test chose for {cell.Timeframe} {cell.Side} is not among the candidates now, so the plan cannot be run on the hold-out."));
                }

                r.AddRange(chosen.Run.Trades.Where(t => t.Trade.Side == side).Select(t => t.Trade.Costing is { } c ? c.RNet : t.Trade.RFilled));
            }
        }

        return Result.Success(new HoldOutFigures(r.Count, r.Count == 0 ? null : r.Average(), block.Start, block.HistoryLast));
    }
}
