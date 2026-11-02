using System.Globalization;
using System.Text.Json;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The gate in front of protocol X (P15-10, criterion 10): the extraction record of each run says what its rows were built from. A <c>fu-1</c> record whose parser version is missing, empty or ends in <c>-synthetic</c>, or any <c>fu-1</c> feature with <c>parser_unverified</c> above zero in the
/// NaN counts, makes the verdict NOT_EVALUABLE <c>parser_unverified</c> whatever the numbers; a record that does not match the run or the current versions makes it NOT_EVALUABLE <c>features_stale</c>. The gate runs before any number is computed. Pure.
/// </summary>
public static class ExtensionGate
{
    /// <summary>The reason for an unverified parser.</summary>
    public const string ParserUnverified = "parser_unverified";

    /// <summary>The reason for rows built from other inputs than the current ones.</summary>
    public const string FeaturesStale = "features_stale";

    /// <summary>The reasons that stop the comparison, empty when the records pass; each starts with its closed code.</summary>
    /// <param name="version">The extension version evaluated.</param>
    /// <param name="records">One entry per run: the run key, its closed trade count and its extraction record (null: no record).</param>
    /// <param name="currentLagMinutes">The lag in force now.</param>
    /// <param name="currentParserVersion">The parser version in force now.</param>
    public static IReadOnlyList<string> Problems(string version, IReadOnlyList<(string RunKey, int ClosedTrades, ExtensionExtractionRecord? Record)> records, int currentLagMinutes, string currentParserVersion)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(currentParserVersion);
        var c = CultureInfo.InvariantCulture;
        var unverified = new List<string>();
        var stale = new List<string>();
        foreach (var (runKey, closed, record) in records.OrderBy(r => r.RunKey, StringComparer.Ordinal))
        {
            var run = runKey.Length >= 12 ? runKey[..12] : runKey;
            if (record is null)
            {
                stale.Add(string.Create(c, $"{FeaturesStale}: run {run} has no extraction record"));
                continue;
            }

            if (version == FundamentalFeatureCatalog.Version)
            {
                if (string.IsNullOrWhiteSpace(record.ParserVersion) || record.ParserVersion.EndsWith("-synthetic", StringComparison.Ordinal))
                {
                    unverified.Add(string.Create(c, $"{ParserUnverified}: run {run} was extracted with parser '{record.ParserVersion ?? "none"}'"));
                }
                else if (ParserUnverifiedCount(record.NanCountsJson) is not { } count)
                {
                    unverified.Add(string.Create(c, $"{ParserUnverified}: the NaN counts of run {run} cannot be read"));
                }
                else if (count > 0)
                {
                    unverified.Add(string.Create(c, $"{ParserUnverified}: {count} values of run {run} were built on an unverified parser"));
                }
                else if (!string.Equals(record.ParserVersion, currentParserVersion, StringComparison.Ordinal))
                {
                    stale.Add(string.Create(c, $"{FeaturesStale}: run {run} was extracted with parser '{record.ParserVersion}', now '{currentParserVersion}'"));
                }
            }

            if (record.RunKey != runKey || record.Version != version)
            {
                stale.Add(string.Create(c, $"{FeaturesStale}: the record of run {run} is for {record.Version}"));
            }
            else if (record.LagMinutes != currentLagMinutes)
            {
                stale.Add(string.Create(c, $"{FeaturesStale}: run {run} was extracted with a lag of {record.LagMinutes} minutes, now {currentLagMinutes}"));
            }
            else if (record.Rows != closed)
            {
                stale.Add(string.Create(c, $"{FeaturesStale}: run {run} has {closed} closed trades and {record.Rows} extracted rows"));
            }
        }

        return [.. unverified, .. stale];
    }

    /// <summary>The sum of the <c>parser_unverified</c> counts of the NaN counts text, or null when the text is not what <see cref="ExtensionNanCounts"/> writes.</summary>
    public static int? ParserUnverifiedCount(string? nanCountsJson)
    {
        if (string.IsNullOrWhiteSpace(nanCountsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(nanCountsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("parser_unverified", out var counts) || counts.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var sum = 0;
            foreach (var feature in counts.EnumerateObject())
            {
                if (feature.Value.ValueKind != JsonValueKind.Number || !feature.Value.TryGetInt32(out var n) || n < 0)
                {
                    return null;
                }

                sum += n;
            }

            return sum;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
