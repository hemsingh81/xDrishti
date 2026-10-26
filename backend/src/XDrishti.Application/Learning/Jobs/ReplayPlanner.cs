using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// Makes a <see cref="ReplayPlan"/> from a stored run (spec P8-18 Part B): reads the record, finds how the strategy is named (a stored version by its definition hash, else a built-in by its id), and PROVES, by compiling and without reading a bar, that the exits it would pass give back
/// the baseline's definition hash. A baseline made with an exit override on a stored strategy has a hash no stored version has: it is refused, not guessed.
/// </summary>
internal sealed class ReplayPlanner(ISimulationEngine engine, IReplayStrategySource source)
{
    public async ValueTask<Result<ReplayPlan>> PlanAsync(StoredRunHeader header, string recordJson, CancellationToken cancellationToken)
    {
        var read = ReplayBaselineReader.Read(header, recordJson);
        if (read.IsFailure)
        {
            return Result.Failure<ReplayPlan>(read.Error);
        }

        var baseline = read.Value;
        var text = await source.StoredSourceAsync(baseline.DefinitionSha256, cancellationToken);
        var stored = text is not null;
        if (!stored && !engine.BuiltIns.Any(b => string.Equals(b.Id, baseline.StrategyId, StringComparison.Ordinal)))
        {
            return Refuse("its strategy is neither a stored version (an exit override of a stored strategy leaves none) nor a built-in");
        }

        var timeframe = Timeframes.All.First(t => t.ToCode() == baseline.Timeframe);
        ExitChoice? own = null;
        if (Reproduces(baseline, text, timeframe, own))
        {
            return Result.Success(Plan(baseline, stored, own));
        }

        if (baseline.Exit is not null)
        {
            var given = new ExitChoice([baseline.Exit], baseline.TimeStop);
            if (Reproduces(baseline, text, timeframe, given))
            {
                return Result.Success(Plan(baseline, stored, given));
            }
        }

        return Refuse("the strategy compiled today does not give the definition the run used");
    }

    private static ReplayPlan Plan(ReplayBaseline baseline, bool stored, ExitChoice? exits) =>
        new(baseline, stored, exits, ReplayKnobCatalogue.Offers(baseline.Exit, baseline.Fills.Slippage));

    private bool Reproduces(ReplayBaseline baseline, string? text, Timeframe timeframe, ExitChoice? exits)
    {
        var prepared = text is null
            ? engine.Prepare(baseline.StrategyId, baseline.Parameters, timeframe, baseline.Fills, exits)
            : engine.PrepareStored(text, baseline.DefinitionSha256, baseline.Parameters, timeframe, baseline.Fills, exits);
        return prepared.IsSuccess && string.Equals(prepared.Value.Record.DefinitionSha256, baseline.DefinitionSha256, StringComparison.Ordinal);
    }

    private static Result<ReplayPlan> Refuse(string reason) => Result.Failure<ReplayPlan>(ExperimentJobErrors.NotRebuildable(reason));
}
