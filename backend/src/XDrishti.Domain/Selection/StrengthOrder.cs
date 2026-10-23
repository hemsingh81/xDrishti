namespace XDrishti.Domain.Selection;

/// <summary>The total order of candidates of equal rank score: stronger first. Expectancy (rounded to 9 places, so float noise never decides), shrunk win rate, trades, then symbol, side and cell key ordinal.</summary>
internal sealed class StrengthOrder : IComparer<Eligibility>
{
    public static StrengthOrder Instance { get; } = new();

    public int Compare(Eligibility? x, Eligibility? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        var c = Round(y.ConservativeExpectancyR).CompareTo(Round(x.ConservativeExpectancyR));
        if (c != 0)
        {
            return c;
        }

        var xs = x.Candidate.Stats;
        var ys = y.Candidate.Stats;
        c = Round(ys?.WinRatePosterior).CompareTo(Round(xs?.WinRatePosterior));
        if (c != 0)
        {
            return c;
        }

        c = (ys?.N ?? 0).CompareTo(xs?.N ?? 0);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(x.Candidate.Symbol, y.Candidate.Symbol);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(x.Candidate.Side, y.Candidate.Side);
        return c != 0 ? c : string.CompareOrdinal(x.Candidate.Cell.Key, y.Candidate.Cell.Key);
    }

    internal static double Round(double? v) => v is { } x ? Math.Round(x, 9) : double.NegativeInfinity;
}
