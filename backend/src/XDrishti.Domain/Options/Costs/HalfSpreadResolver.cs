using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// The half-spread of a leg (spec P14-07): the measured model's bucket when there is a model and the bucket has a row, else the owner's assumption for the underlying when he set one, else <see cref="HalfSpreadValue.Unavailable"/>. Nothing defaults to a
/// number; the assumption is the owner's setting and nothing in the repository. Every answer carries its label.
/// </summary>
public static class HalfSpreadResolver
{
    public static HalfSpreadValue Resolve(HalfSpreadModel? model, decimal? assumedPct, SpreadBucket? bucket, DateOnly legDate)
    {
        if (model is not null && bucket is not null && model.Lookup(bucket) is { } row)
        {
            var recent = legDate < model.FirstSession;
            var label = string.Create(CultureInfo.InvariantCulture, $"spread: measured ({model.Sessions} sessions, from {model.FirstSession:yyyy-MM-dd} to {model.LastSession:yyyy-MM-dd})")
                + (recent ? "; " + HalfSpreadValue.SpreadFromRecentArchive : string.Empty);
            return new HalfSpreadValue(HalfSpreadSource.Measured, (decimal)row.HalfSpreadPct, label, null, recent);
        }

        if (assumedPct is { } assumed && assumed > 0)
        {
            return new HalfSpreadValue(HalfSpreadSource.Assumed, assumed, "spread: assumed " + CanonicalText.Number(assumed) + " % by owner", null, false);
        }

        return HalfSpreadValue.Unavailable;
    }
}
