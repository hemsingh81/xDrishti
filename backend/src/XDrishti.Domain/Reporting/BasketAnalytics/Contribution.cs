namespace XDrishti.Domain.Reporting;

/// <summary>
/// Each member's share of the basket's buy-and-hold return (BK-06 m15): share_i = (R_i / N) / R_B, with R_i and R_B from <see cref="BuyAndHold"/> (P8-07), so the shares tie to the headline figure and sum to 1. When R_B is zero the shares are undefined and say so. Pure.
/// </summary>
public static class Contribution
{
    private const double Zero = 1e-9;

    public static ContributionResult Compute(IReadOnlyList<MemberSeries> members, ProfileWindow window, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        var hold = new BuyAndHoldOptions(options.MinMembersForReturn, options.HoldMaxGapDays, options.DiscontinuityThreshold);
        var series = members.Select(m => new InstrumentSeries(Guid.Empty, m.Symbol, m.Closes)).ToList();
        var overall = BuyAndHold.Compute(series, window.From, window.To, hold);
        if (overall.Return.Number is not { } total)
        {
            return new ContributionResult(overall.Return, overall.Return, overall.Included, [], [], [], overall.Excluded);
        }

        if (Math.Abs(total) < Zero)
        {
            return new ContributionResult(overall.Return, Metric.NotEvaluable("return is zero; shares undefined"), overall.Included, [], [], [], overall.Excluded);
        }

        var single = new BuyAndHoldOptions(1, options.HoldMaxGapDays, options.DiscontinuityThreshold);
        var excluded = overall.Excluded.Select(e => e.Symbol).ToHashSet(StringComparer.Ordinal);
        var rows = new List<ContributionRow>(overall.Included);
        foreach (var s in series.OrderBy(x => x.Symbol, StringComparer.Ordinal))
        {
            if (!excluded.Contains(s.Symbol) && BuyAndHold.Compute([s], window.From, window.To, single).Return.Number is { } r)
            {
                rows.Add(new ContributionRow(s.Symbol, r, r / overall.Included / total));
            }
        }

        var ranked = rows.OrderByDescending(r => r.Share).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToList();
        var top3 = ranked.Take(3).Sum(r => r.Share);
        return new ContributionResult(overall.Return, Metric.Of(top3), overall.Included, ranked, [.. ranked.Take(5)], [.. ranked.AsEnumerable().Reverse().Take(5)], overall.Excluded);
    }
}
