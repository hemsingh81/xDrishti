using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Learning;

/// <summary>
/// How the regime labels of a set of trades (or days) came out (P6-02): how many have a label, by basis, and how many have none, by reason. Every output that uses regimes prints it, so a thin or missing history is visible.
/// </summary>
/// <param name="Labelled">Labels that exist.</param>
/// <param name="IndexAndVix">Of those, built from the index and the VIX percentile.</param>
/// <param name="IndexOnly">Of those, built from the index percentile alone.</param>
/// <param name="UnavailableByReason">Labels that do not exist, by reason, in ordinal order of the reason.</param>
public sealed record RegimeTally(int Labelled, int IndexAndVix, int IndexOnly, IReadOnlyDictionary<string, int> UnavailableByReason)
{
    public int Unavailable => UnavailableByReason.Values.Sum();

    public int Total => Labelled + Unavailable;

    public static RegimeTally Of(IEnumerable<RegimeLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var labelled = 0;
        var both = 0;
        var indexOnly = 0;
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            if (label.IsAvailable)
            {
                labelled++;
                if (label.Basis == ContextBasis.IndexOnly)
                {
                    indexOnly++;
                }
                else
                {
                    both++;
                }
            }
            else
            {
                var reason = label.Unavailable ?? ContextRead.NoContext;
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
        }

        return new RegimeTally(labelled, both, indexOnly, reasons);
    }
}
