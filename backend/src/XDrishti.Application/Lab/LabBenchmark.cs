using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Market;
using XDrishti.Domain.Readiness;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// The benchmark of row 13 of the readiness scorecard (CF-11, ADR 0227): the daily Sharpe of the out-of-sample net R of a full Lab run (summed by exit day, 0 on a day with no exit) against the daily Sharpe of the equal-weight, no-rebalancing buy-and-hold of the run's universe over the identical
/// days, using the pure <see cref="BuyAndHold"/> rules of P8-07 for which members count. One bounded read of the stored day bars per instrument. Anything that stops the comparison (a sampled universe, no trade, a member the hold cannot use, too few days, no variance, a failed read) gives a block with
/// no figures and the reason, which the scorecard counts as a fail; cancellation propagates. Report-only: the verdict is not touched.
/// </summary>
internal sealed class LabBenchmark(IBarQueries bars, ReportBenchmarkOptions settings, ILogger<LabBenchmark> logger)
{
    /// <summary>What is measured and what is not, printed with the figures.</summary>
    public const string Basis =
        "Daily Sharpe (mean over sample standard deviation, not annualised, no risk-free rate) of the out-of-sample net R summed by exit day, against the equal-weight buy-and-hold of the run's whole universe (price return, no rebalancing, dividends not included) over the same sessions. "
        + "The strategy's R is a return up to a constant, which a Sharpe ignores. The hold's members are the run's instruments as listed; a member it cannot use makes the comparison unavailable. Members are as of the run, so survivorship (ISS-22) flatters the hold. "
        + "Summing R by exit day is a bias, not a neutral choice: trades that exit on one day lump together and inflate the variance, which biases the strategy's Sharpe down (conservative); summing concurrent trades is variable leverage, so when the overlap varies the bias can go either way.";

    /// <summary>Calendar days read before the first exit, so the hold has a purchase close before the first day compared. It must stay at least the hold's <c>MaxStartGapDays</c> (see <see cref="LookBack"/>), or a first bar inside the allowed gap would lie before the window read.</summary>
    private const int LookBackDays = 10;

    private int LookBack => Math.Max(LookBackDays, settings.MaxStartGapDays);

    public async ValueTask<BenchmarkComparisonView> ComputeAsync(LabUniverse universe, IReadOnlyList<SelectionTrade> trades, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(trades);
        var members = universe.Instruments.Count;
        if (universe.Sampled)
        {
            return Unavailable(members, "the universe is a sample of the basket, not the whole universe");
        }

        if (trades.Count == 0)
        {
            return Unavailable(members, "the verdict has no out-of-sample trade");
        }

        var first = trades.Min(t => t.Exit);
        var last = trades.Max(t => t.Exit);
        var from = first.AddDays(-LookBack);
        var series = new List<InstrumentSeries>(members);
        try
        {
            // shortcut: one bounded daily read per instrument (IBarQueries has no multi-instrument daily read); upgrade to one batched read when the universe passes 100 instruments
            foreach (var instrument in universe.Instruments.OrderBy(i => i.Symbol, StringComparer.Ordinal).ThenBy(i => i.Id))
            {
                var rows = await bars.ReadDailyBarsAsync(instrument.Id, from, last.AddDays(1), cancellationToken);
                series.Add(new InstrumentSeries(instrument.Id, instrument.Symbol, [.. rows.Select(r => new DailyClose(IndiaTime.Today(r.Ts), r.Close))]));
            }
        }
#pragma warning disable CA1031 // the benchmark block is report-only: a failed read makes the comparison unavailable (a fail on the scorecard) and must not fail the Lab run (a cancel requested on the token still propagates; a driver timeout does not)
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            LabRunLog.BenchmarkUnread(logger, members, ex);
            return Unavailable(members, "the daily bars of the universe could not be read (" + ex.GetType().Name + ")");
        }

        var options = new BuyAndHoldOptions(settings.MinMembers, settings.MaxStartGapDays, settings.DiscontinuityThreshold);
        var hold = BuyAndHoldDaily.Compute(series, from, last, first, options);
        if (hold.Reason.Length > 0)
        {
            return Unavailable(members, hold.Reason);
        }

        var strategy = BenchmarkComparison.StrategyDaily([.. trades.Select(t => (t.Exit, t.R))], hold.Days);
        if (strategy is null)
        {
            return Unavailable(members, "a trade exits on a session on which not every member has a close, so the days are not the strategy's days");
        }

        var comparison = BenchmarkComparison.Of(strategy, hold.Returns);
        return new BenchmarkComparisonView(comparison.Days, members, comparison.StrategySharpe, comparison.HoldSharpe, comparison.Reason, Basis);
    }

    private static BenchmarkComparisonView Unavailable(int members, string reason) => new(0, members, null, null, reason, Basis);
}
