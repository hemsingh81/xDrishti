using System.Text.Json;
using System.Text.Json.Serialization;

namespace XDrishti.Domain.Filings;

/// <summary>
/// <c>sample.json</c> (P15-07): sampler version, seed, range, the baseline's hash, the strata counts, and every row with its inclusion weight and the hash of its text columns. It holds no announcement text. <see cref="ToJson"/> is deterministic: the same sample gives the
/// same bytes.
/// </summary>
public sealed record AnnouncementSampleFile(
    string SamplerVersion, long Seed, string From, string To, string BaselineSha256, int Eligible, int EligibleMaterial, int EligibleOther, int NoMetadata, int MainCount, int ReserveCount,
    IReadOnlyList<SampleStratum> Strata, IReadOnlyList<SampleFileRow> Rows)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public string ToJson() => JsonSerializer.Serialize(this, Options) + "\n";

    /// <summary>Reads a sample file; null when it is not one.</summary>
    public static AnnouncementSampleFile? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var file = JsonSerializer.Deserialize<AnnouncementSampleFile>(json, Options);
            return file is { Rows: { } rows, Strata: not null } && rows.All(r => r is not null) ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
