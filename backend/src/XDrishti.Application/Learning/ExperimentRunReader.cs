using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>Reads the closed trades of one stored run as the experiments see them: the India date of the entry and the R (net of charges when the run is costed, else filled). Shared by the experiment and the phase check (P6-18).</summary>
internal static class ExperimentRunReader
{
    internal sealed record Seen(DateOnly TradeDate, double R, long DecisionEpochSeconds, long ExitEpochSeconds, bool Costed);

    public static async ValueTask<Result<Dictionary<string, Seen>>> ReadAsync(IRunStore runs, string runKey, CancellationToken cancellationToken)
    {
        var seen = new Dictionary<string, Seen>(StringComparer.Ordinal);
        await foreach (var runTrade in runs.StreamTradesAsync(runKey, false, cancellationToken))
        {
            var t = runTrade.Trade;
            var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
            var trade = new Seen(SessionClock.DateOf(t.DecisionEpochSeconds - HoldOut.BarSeconds), costed ? t.Costing!.RNet : t.RFilled, t.DecisionEpochSeconds, t.ExitEpochSeconds, costed);
            if (!seen.TryAdd(t.Key, trade))
            {
                return Result.Failure<Dictionary<string, Seen>>(ExperimentErrors.DuplicateTrade(runKey));
            }
        }

        return Result.Success(seen);
    }
}
