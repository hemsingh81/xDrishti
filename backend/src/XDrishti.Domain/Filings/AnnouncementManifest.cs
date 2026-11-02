using System.Text.Json;
using System.Text.Json.Serialization;

namespace XDrishti.Domain.Filings;

/// <summary>
/// <c>eval/sets/announcements.manifest.json</c> (P15-07), the only part of the labelled set that is committed: the ids, filing ids and row hashes of the 200 labelled rows, the hash of the label file, the sampler version, the seed, the baseline's hash and the class counts. No text.
/// </summary>
public sealed record AnnouncementManifest(
    string SamplerVersion, long Seed, string BaselineSha256, string LabelFileSha256, IReadOnlyDictionary<string, int> ClassCounts, IReadOnlyList<ManifestRow> Rows)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static AnnouncementManifest Build(AnnouncementSampleFile sample, string labelFileSha256, IReadOnlyList<LabelledPick> picks)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(picks);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            [AnnouncementCategoryBaseline.Material] = picks.Count(p => p.Label == AnnouncementCategoryBaseline.Material),
            [AnnouncementCategoryBaseline.NonMaterial] = picks.Count(p => p.Label == AnnouncementCategoryBaseline.NonMaterial),
        };
        return new AnnouncementManifest(sample.SamplerVersion, sample.Seed, sample.BaselineSha256, labelFileSha256, counts, [.. picks.Select(p => new ManifestRow(p.Row.Id, p.Row.FilingId, p.Row.RowSha256))]);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options) + "\n";
}
