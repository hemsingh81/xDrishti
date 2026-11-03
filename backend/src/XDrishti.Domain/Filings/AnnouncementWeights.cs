using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The inclusion weights of the labelled rows (P15-08, left open by P15-07): a row that replaced an <c>unsure</c> one keeps the weight of the stratum it was drawn in, which is not the weight of the final 200. So the weight is recomputed from the labelled rows: for the stratum
/// (half, category, India year) of a row it is the stratum's eligible count in <c>sample.json</c> over the number of labelled rows now in that stratum. A row whose stratum or sample row is unknown gets weight 1 for itself alone (never a guess). Pure.
/// </summary>
public static class AnnouncementWeights
{
    /// <summary>The weight of each labelled row by id.</summary>
    public static IReadOnlyDictionary<string, double> Recompute(AnnouncementSampleFile sample, IReadOnlyList<LabelledAnnouncement> rows)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(rows);
        var halfOf = sample.Rows.GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Half, StringComparer.Ordinal);
        var eligible = sample.Strata.GroupBy(s => (s.Half, s.Category, s.Year)).ToDictionary(g => g.Key, g => g.First().Eligible);
        var keyed = rows.Select(r => (Row: r, Key: KeyOf(r, halfOf))).ToList();
        var counts = keyed.Where(k => k.Key is not null).GroupBy(k => k.Key!.Value).ToDictionary(g => g.Key, g => g.Count());
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (row, key) in keyed)
        {
            weights[row.Id] = key is { } k && eligible.TryGetValue(k, out var total) && counts[k] > 0 ? Math.Round((double)total / counts[k], 6) : 1.0;
        }

        return weights;
    }

    private static (string Half, string Category, int Year)? KeyOf(LabelledAnnouncement row, Dictionary<string, string> halfOf) =>
        row.Category is { } category && halfOf.TryGetValue(row.Id, out var half) && DateTimeOffset.TryParse(row.PublishedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at)
            ? (half, category, AnnouncementSampler.YearOf(at))
            : null;
}
