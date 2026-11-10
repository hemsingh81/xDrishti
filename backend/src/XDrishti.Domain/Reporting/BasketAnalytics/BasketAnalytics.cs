using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The one entry of the basket performance engine (BK-06): membership by date and the members' daily bars in, the profile with the honest state of every figure out. Pure: no I/O, no clock, no setting, no write; the same input gives the same result.
/// </summary>
public static class BasketAnalytics
{
    public static BasketProfile Profile(BasketProfileInput input, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        var window = input.Window;
        // A member with no valid bar up to the window end is not part of anything yet (a later start changes nothing before it).
        var members = input.Members.Where(m => BasketStats.Clean(m.Closes, DateOnly.MinValue, window.To).Dates.Length > 0).OrderBy(m => m.Symbol, StringComparer.Ordinal).ToList();
        var index = EqualWeightIndex.Build(members, input.MembershipAt, window, options);
        var benchmarkReturns = ReturnSeries.FromCloses(input.Benchmark.Select(c => (c.Date, c.Close)), window.To);
        var relation = BenchmarkRegression.Compute(index, benchmarkReturns, options);
        var turnover = MemberTurnover.Compute(input.MembershipDates, input.MembershipAt, window);

        DateOnly? asOf = null;
        foreach (var m in members)
        {
            var (dates, _, _) = BasketStats.Clean(m.Closes, window.From, window.To);
            if (dates.Length > 0 && (asOf is null || dates[^1] > asOf))
            {
                asOf = dates[^1];
            }
        }

        var current = asOf is { } d ? members.Where(m => input.MembershipAt(d).Contains(m.Symbol)).ToList() : [];
        var last = asOf ?? window.To;
        var returns = current.ToDictionary(m => m.Symbol, m => ReturnSeries.FromCloses(m.Closes.Select(c => (c.Date, c.Close)), last), StringComparer.Ordinal);
        var capacity = BasketCapacity.Compute(current, last, options);
        // The hold return is what one could have bought at the window start: the membership in force at From, not today's (survivorship, ISS-22).
        var startSet = input.MembershipAt(window.From);
        var contribution = Contribution.Compute([.. members.Where(m => startSet.Contains(m.Symbol))], window, options);
        var indexHold = BuyAndHold.Compute([new InstrumentSeries(Guid.Empty, "index", input.Benchmark)], window.From, window.To, new BuyAndHoldOptions(1, options.HoldMaxGapDays, options.DiscontinuityThreshold)).Return;

        return new BasketProfile(
            asOf,
            index,
            RiskMetrics.Volatility(index, options.VolatilityWindows),
            RiskMetrics.Drawdown(index, options.MinDrawdownPoints),
            relation,
            RelativeStrength.Compute(index, input.Benchmark, options.RelativeStrengthWindows),
            asOf is null ? BasketBreadth.Compute([], last, options) : BasketBreadth.Compute(current, last, options),
            Dispersion.Compute(returns, last, options),
            PairwiseCorrelation.Compute(returns, capacity.AdvBySymbol, last, options),
            turnover,
            capacity,
            contribution,
            RegimeSplit.Compute(index, benchmarkReturns, input.Regimes, options),
            contribution.ReturnPct,
            indexHold,
            contribution.ReturnPct.Number is { } basket && indexHold.Number is { } passive ? Metric.Of(basket - passive) : Metric.Unavailable("the basket or the index has no return for the window"),
            turnover.Basis,
            turnover.FirstDate);
    }
}
