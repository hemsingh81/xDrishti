using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Paper;

/// <summary>
/// The weekly review's backtest line (spec P10-03 port, P10-04 report): one sentence read from <see cref="GetBacktestVsPaperQuery"/>. When the report cannot place the paper mean (too few trades, an unavailable reference, a basis difference or a thin
/// reference) the line says so with the report's reason and prints no mean, no band and no interval. A placed mean is printed with its position, the verdicts and whether costs are confirmed. It is simulated and never an instruction to trade.
/// </summary>
internal sealed class BacktestReviewComparisonLine(IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>> comparison, ILogger<BacktestReviewComparisonLine> logger) : IReviewComparisonLine
{
    private const string Tail = " Simulated on both sides; not an instruction to trade.";

    public async ValueTask<string> LineAsync(long epochId, CancellationToken cancellationToken)
    {
        var result = await comparison.HandleAsync(new GetBacktestVsPaperQuery(epochId, null), cancellationToken);
        if (result.IsFailure)
        {
            PaperPhaseLog.ComparisonLine(logger, epochId, "unavailable");
            return "unavailable: the backtest comparison could not be read (" + result.Error.Code + "), so no position against the band is given.";
        }

        var v = result.Value;
        if (v.EpochId is null)
        {
            PaperPhaseLog.ComparisonLine(logger, epochId, "no_epoch");
            return "unavailable: no paper epoch yet, so no position against the backtest band is given.";
        }

        var costs = v.ChargeSheet.ConfirmedOn is null ? " Costs are not confirmed by the owner, so the exit statistics are not evaluable." : string.Empty;
        if (v.Band.P05.Number is null || v.Band.P95.Number is null || v.Paper.Mean.Number is null || v.Band.Position is null)
        {
            var gone = v.Band.P05;
            PaperPhaseLog.ComparisonLine(logger, epochId, gone.State);
            return gone.State + ": no position against the backtest band is given" + (gone.Reason is null ? string.Empty : " (" + gone.Reason + ")") + "." + costs + Tail;
        }

        PaperPhaseLog.ComparisonLine(logger, epochId, v.Band.Position);
        var mean = v.Paper.Mean.Number.Value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        var lo = v.Band.P05.Number.Value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        var hi = v.Band.P95.Number.Value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        var provisional = v.Paper.Mean.State == "provisional" ? " (provisional)" : string.Empty;
        var warning = v.EarlyWarning is null ? string.Empty : " EARLY WARNING: " + v.EarlyWarning + ".";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Paper mean {mean}R{provisional} over {v.N} counted trades is {v.Band.Position} the backtest band [{lo}R, {hi}R]; expectancy {v.Criteria.Expectancy.Verdict}, drawdown {v.Criteria.Drawdown.Verdict}.{costs}{warning}{Tail}");
    }
}
