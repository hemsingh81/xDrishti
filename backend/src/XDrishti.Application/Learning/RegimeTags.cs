using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Learning;

/// <summary>
/// The regime label of each date, from one batch read of the stored context (P6-02, ADR 0146): the label of a decision day is the regime of the latest stored day strictly before it, or the reason there is none. Reads, writes nothing.
/// </summary>
internal static class RegimeTags
{
    public static async ValueTask<IReadOnlyDictionary<DateOnly, RegimeLabel>> LabelsAsync(IContextFigures figures, IEnumerable<DateOnly> dates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(dates);
        var distinct = dates.Distinct().ToList();
        var reads = await figures.AsOfManyAsync(distinct, cancellationToken);
        var labels = new Dictionary<DateOnly, RegimeLabel>(distinct.Count);
        foreach (var date in distinct)
        {
            labels[date] = RegimeLabel.Of(date, reads.TryGetValue(date, out var read) ? read : ContextRead.Not(ContextRead.NoContext));
        }

        return labels;
    }

    /// <summary>The text that names the labels used, for the key of a snapshot: the date and the cell part or the reason, in date order.</summary>
    public static string Canonical(IReadOnlyDictionary<DateOnly, RegimeLabel> labels) =>
        string.Join(';', labels.OrderBy(l => l.Key).Select(l => l.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "=" + (l.Value.IsAvailable ? l.Value.CellPart : "-" + l.Value.Unavailable)));
}
