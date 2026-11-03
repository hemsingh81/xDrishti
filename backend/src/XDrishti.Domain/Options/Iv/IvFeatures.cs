using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Options.Iv;

/// <summary>
/// The features of <c>iv-1</c> at a decision instant (P14-11, ADR 0199, pure), in the order of <see cref="IvFeatureCatalog.Names"/>. Only what is public at the instant is read: bars that have ended at or before it on its own India date, daily values of earlier sessions, closes of earlier sessions
/// and calendar rows visible under the point-in-time rule. Everything unknown is NaN with a closed reason from <see cref="IvReasons"/> or <see cref="FeatureReasons"/>; nothing is filled, defaulted or taken from a neighbour.
/// </summary>
public static class IvFeatures
{
    /// <summary>The offsets whose open interest makes the put-call ratio.</summary>
    public const int OiOffsets = 3;

    public static FeatureCell[] Compute(IvFeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var tradeDate = IndiaTime.Today(input.At);
        var front = input.Plan is { } plan ? Atm(input, plan.FrontCode) : FeatureCell.Unknown(NoSeries(input));
        var rank = input.OwnDaily is null ? NoOptionsRank : IvRank.OfSeries(input.OwnDaily, tradeDate, input.Calendar);
        var nifty = input.NiftyDaily is null ? NoOptionsRank : IvRank.OfSeries(input.NiftyDaily, tradeDate, input.Calendar);
        return
        [
            front,
            Cell(rank.Rank, rank.Reason),
            Cell(rank.Percentile, rank.Reason),
            TermSlope(input, front),
            MinusRv(input, front, tradeDate),
            Cell(nifty.Rank, nifty.Reason),
            Pcr(input),
            ResultsBeforeExpiry(input),
        ];
    }

    private static readonly IvRankResult NoOptionsRank = new(double.NaN, double.NaN, IvReasons.NoOptions);

    // An option underlying of the archive (it has a daily series or bars) with no eligible series today, against an instrument that is not an underlying at all.
    private static string NoSeries(IvFeatureInput input) => input.OwnDaily is null && input.Bars is null ? IvReasons.NoOptions : IvReasons.NoEligibleSeries;

    private static FeatureCell Cell(double value, string? reason) => reason is null ? FeatureCell.Known(value) : FeatureCell.Unknown(reason);

    private static FeatureCell TermSlope(IvFeatureInput input, FeatureCell front)
    {
        if (input.Plan is null)
        {
            return FeatureCell.Unknown(NoSeries(input));
        }

        if (input.Plan.BackCode is not { } backCode)
        {
            return FeatureCell.Unknown(IvReasons.NoTermSeries);
        }

        if (!front.IsKnown)
        {
            return front;
        }

        var back = Atm(input, backCode);
        return back.IsKnown ? FeatureCell.Known(back.Value - front.Value) : back;
    }

    private static FeatureCell MinusRv(IvFeatureInput input, FeatureCell front, DateOnly tradeDate)
    {
        if (!front.IsKnown)
        {
            return front;
        }

        var rv = RealisedVolatility.Annualised(input.Closes, tradeDate, input.Calendar, input.Actions);
        return rv.IsKnown ? FeatureCell.Known(front.Value - rv.Value) : rv;
    }

    // The mean of the CE and PE offset-0 IV (a fraction) of the last bar of the code that has ended by the instant. Order of the reasons: no bar at all, then an unusable bar, then a missing or invalid IV.
    private static FeatureCell Atm(IvFeatureInput input, int code)
    {
        if (input.Bars is null)
        {
            return FeatureCell.Unknown(IvReasons.NoBarBeforeT);
        }

        var (sliceCe, slicePe) = input.Bars.LastPair(code, input.At);
        if (sliceCe is null && slicePe is null)
        {
            return FeatureCell.Unknown(IvReasons.NoBarBeforeT);
        }

        var ce = sliceCe?.At(0);
        var pe = slicePe?.At(0);

        if ((ce is not null && (!ce.Usable || ce.Suspect)) || (pe is not null && (!pe.Usable || pe.Suspect)))
        {
            return FeatureCell.Unknown(IvReasons.ArchiveUnusable);
        }

        return DailyAtmIv.Mean(ce?.Iv, pe?.Iv) is { } mean ? FeatureCell.Known(mean) : FeatureCell.Unknown(IvReasons.IvMissing);
    }

    private static FeatureCell Pcr(IvFeatureInput input)
    {
        if (input.Plan is not { } plan)
        {
            return FeatureCell.Unknown(NoSeries(input));
        }

        var (ce, pe) = input.Bars?.LastPair(plan.FrontCode, input.At) ?? (null, null);
        if (ce is null && pe is null)
        {
            return FeatureCell.Unknown(IvReasons.NoBarBeforeT);
        }

        long callOi = 0;
        long putOi = 0;
        var unusable = false;
        var missing = ce is null || pe is null;
        for (var offset = -OiOffsets; offset <= OiOffsets; offset++)
        {
            var c = ce?.At(offset);
            var p = pe?.At(offset);
            unusable |= (c is not null && (!c.Usable || c.Suspect)) || (p is not null && (!p.Usable || p.Suspect));
            if (c?.Oi is not { } coi || p?.Oi is not { } poi)
            {
                missing = true;
                continue;
            }

            callOi += coi;
            putOi += poi;
        }

        if (unusable)
        {
            return FeatureCell.Unknown(IvReasons.ArchiveUnusable);
        }

        if (missing)
        {
            return FeatureCell.Unknown(IvReasons.OiMissing);
        }

        return callOi <= 0 ? FeatureCell.Unknown(IvReasons.ZeroCallOi) : FeatureCell.Known(putOi / (double)callOi);
    }

    private static FeatureCell ResultsBeforeExpiry(IvFeatureInput input)
    {
        if (input.Plan is not { } plan)
        {
            return FeatureCell.Unknown(NoSeries(input));
        }

        var answer = ResultsDays.Compute(input.CalendarRows, input.At, input.Lag, input.Calendar);
        return answer.State == ResultsAnswer.Value && answer.EventSession is { } session
            ? FeatureCell.Known(session <= plan.FrontExpiry ? 1 : 0)
            : FeatureCell.Unknown(IvReasons.EventUnavailable);
    }
}
