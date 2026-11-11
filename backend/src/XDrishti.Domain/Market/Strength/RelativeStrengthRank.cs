using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Market.Strength;

/// <summary>
/// The volatility-adjusted momentum rank inside a basket (BK-09 <c>mom_rank</c>): R = c(t-s)/c(t-L) - 1, v = sample stdev of the daily returns over those L - s sessions, S = R / v, percent rank p = (members with a lower score + 0.5 x
/// ties excluding self) / (N - 1). Only the closes up to <c>asOf</c> are read and the latest <c>s</c> sessions never enter R. Pure.
/// </summary>
public static class RelativeStrengthRank
{
    /// <summary>The reason a share has no score because its window holds an ex-date (the BreadthCalculator rule).</summary>
    public const string CorporateActionUnresolved = "corporate_action_unresolved";

    /// <summary>
    /// The momentum return and volatility of one share as of <paramref name="asOf"/>; a state when it has none. The share's own sessions are counted, so it needs a close on <paramref name="asOf"/>. A share with an ex-date of a corporate action after the first close
    /// of the window and up to <paramref name="asOf"/> is not evaluable (<see cref="CorporateActionUnresolved"/>): it is excluded and counted, never repaired.
    /// </summary>
    public static (MomentumInput? Value, Metric? Why) Momentum(MemberSeries member, DateOnly asOf, int lookback, int skip, IReadOnlyCollection<DateOnly>? exDates = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (lookback <= skip || skip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback), lookback, "The lookback must exceed the skip.");
        }

        var (dates, closes, _) = BasketStats.Clean(member.Closes, DateOnly.MinValue, asOf);
        if (dates.Length == 0 || dates[^1] != asOf)
        {
            return (null, Metric.Unavailable("no close on the day"));
        }

        var t = dates.Length - 1;
        if (t < lookback)
        {
            return (null, Metric.Unavailable($"short_history: {t + 1} closes, {lookback + 1} needed"));
        }

        if (exDates is not null && exDates.Any(x => x > dates[t - lookback] && x <= asOf))
        {
            return (null, Metric.NotEvaluable(CorporateActionUnresolved));
        }

        var returns = new double[lookback - skip];
        for (var k = 0; k < returns.Length; k++)
        {
            returns[k] = (closes[t - lookback + k + 1] / closes[t - lookback + k]) - 1.0;
        }

        var r = (closes[t - skip] / closes[t - lookback]) - 1.0;
        return (new MomentumInput(member.Symbol, r, BasketStats.Stdev(returns)), null);
    }

    /// <summary>Ranks the inputs. A zero or non-finite volatility is not evaluable (never a division by zero) and the share is left out and counted; fewer than <see cref="StrengthOptions.MinVoters"/> scores leave every rank insufficient.</summary>
    public static RankResult Rank(IReadOnlyList<MomentumInput> inputs, StrengthOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);
        var scored = new List<(string Symbol, double Score)>(inputs.Count);
        var rows = new List<RankRow>(inputs.Count);
        foreach (var i in inputs)
        {
            var score = i.Return / i.Volatility;
            if (!double.IsFinite(i.Volatility) || i.Volatility <= 0 || !double.IsFinite(score))
            {
                var why = Metric.NotEvaluable("zero or unknown volatility over the window");
                rows.Add(new RankRow(i.Symbol, why, why));
                continue;
            }

            scored.Add((i.Symbol, score));
        }

        var n = scored.Count;
        foreach (var (symbol, score) in scored)
        {
            if (n < options.MinVoters)
            {
                rows.Add(new RankRow(symbol, Metric.Of(score), Metric.Insufficient($"n = {n}, at least {options.MinVoters} needed")));
                continue;
            }

            var lower = scored.Count(s => s.Score < score);
            var equal = scored.Count(s => s.Score == score) - 1;
            rows.Add(new RankRow(symbol, Metric.Of(score), Metric.Of((lower + (0.5 * equal)) / (n - 1))));
        }

        return new RankResult([.. rows.OrderBy(r => r.Symbol, StringComparer.Ordinal)], n, inputs.Count - n);
    }
}
