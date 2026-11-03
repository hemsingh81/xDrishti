using System.Globalization;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// The trials of protocol S (P14-13, decision 1): a candidate pair is one trial (family, the ordered symbols, the test definition) and each of the six trading variants is one. The text names the pair and the test, not the window, so a pair re-formed in a later quarter has the same hash (no new look); the
/// ledger run id includes the window, so a new window is a new row with the same hash. The caller appends the row before it computes the statistic.
/// </summary>
public static class PairTrial
{
    /// <summary>The canonical text of a candidate: <c>y</c> first, the test with its lags, q and half-life range.</summary>
    public static string CandidateText(string y, string x, PairStudyOptions options)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(options);
        var c = options.Core;
        return string.Join(
            '\n',
            "pair-trial/1",
            "y:" + y,
            "x:" + x,
            string.Create(CultureInfo.InvariantCulture, $"test:engle-granger lags {c.Lags} q {c.Q:R} half-life {c.HalfLifeMinSessions:R}..{c.HalfLifeMaxSessions:R} sessions on {options.FormationSessions} daily closes"));
    }

    /// <summary>The canonical text of a trading variant: entry level, look-back, exit, stop and the session rules.</summary>
    public static string VariantText(PairVariant variant, PairStudyOptions options)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(options);
        var c = options.Core;
        return string.Join(
            '\n',
            "pair-variant/1",
            "variant:" + variant.Label,
            string.Create(CultureInfo.InvariantCulture, $"rule:5-minute spread z against the previous {variant.Lookback} sessions; enter entry<=|z|<stop at {variant.Entry:R}/{c.Stop:R}; exit |z|<={c.Exit:R}; flat {options.SquareOffMinute}; no entry after {options.LastEntryMinute}"),
            "walker:pair-1");
    }

    /// <summary>The trial of a candidate pair on the formation window <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static ExtensionTrialId Candidate(string y, string x, PairStudyOptions options, DateOnly from, DateOnly to) => ExtensionFamilies.TrialOf(PairFamilies.Intraday, CandidateText(y, x, options), from, to);

    /// <summary>The trial of a trading variant on the data window <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static ExtensionTrialId Variant(PairVariant variant, PairStudyOptions options, DateOnly from, DateOnly to) => ExtensionFamilies.TrialOf(PairFamilies.Intraday, VariantText(variant, options), from, to);
}
