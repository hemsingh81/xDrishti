using System.Globalization;
using System.Text.Json;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// What an <c>iv-1</c> extraction depended on besides the record's columns (P14-11, ADR 0199): the archive watermark (the highest finished fetch-log id read), the ids of the quality runs that decided the usable key-sessions and the IV unit label. Written into the <c>sources</c> object of the extraction record
/// (<see cref="ExtensionNanCounts"/>), read back and merged over the runs of an evaluation so that the evaluation record names them. A later watermark than the one stored marks the record stale for the later review (P14-17). Pure.
/// </summary>
public static class IvSourceNotes
{
    public const string ArchiveWatermark = "archive_watermark";

    public const string QualityRunIds = "quality_run_ids";

    public const string IvUnit = "iv_unit";

    /// <summary>The word for a part no record carries.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The notes of one extraction as the text values the record keeps.</summary>
    public static IReadOnlyDictionary<string, string> Of(long archiveWatermark, IEnumerable<long> qualityRunIds, string ivUnit)
    {
        ArgumentNullException.ThrowIfNull(qualityRunIds);
        ArgumentNullException.ThrowIfNull(ivUnit);
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [ArchiveWatermark] = archiveWatermark.ToString(CultureInfo.InvariantCulture),
            [QualityRunIds] = string.Join(',', qualityRunIds.Distinct().Order().Select(i => i.ToString(CultureInfo.InvariantCulture))),
            [IvUnit] = ivUnit,
        };
    }

    /// <summary>The notes of an extraction record's counts text; empty when it has none or cannot be read.</summary>
    public static IReadOnlyDictionary<string, string> Read(string? nanCountsJson)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(nanCountsJson))
        {
            return notes;
        }

        try
        {
            using var document = JsonDocument.Parse(nanCountsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in sources.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    notes[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            notes.Clear();
        }

        return notes;
    }

    /// <summary>The notes of several runs as the evaluation record names them: the highest watermark, the union of the quality run ids (ascending), the distinct unit labels; <c>unavailable</c> for a part no record carries.</summary>
    public static IReadOnlyDictionary<string, string> Merge(IEnumerable<IReadOnlyDictionary<string, string>> perRun)
    {
        ArgumentNullException.ThrowIfNull(perRun);
        var all = perRun.ToList();
        var watermarks = all.Select(n => n.TryGetValue(ArchiveWatermark, out var w) && long.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? (long?)v : null).Where(v => v is not null).ToList();
        var ids = all.SelectMany(n => n.TryGetValue(QualityRunIds, out var list) ? list.Split(',', StringSplitOptions.RemoveEmptyEntries) : [])
            .Select(i => long.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? (long?)v : null).Where(v => v is not null).Select(v => v!.Value).Distinct().Order().ToList();
        var units = all.Select(n => n.GetValueOrDefault(IvUnit)).Where(u => !string.IsNullOrEmpty(u)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [ArchiveWatermark] = watermarks.Count == 0 ? Unavailable : watermarks.Max()!.Value.ToString(CultureInfo.InvariantCulture),
            [QualityRunIds] = ids.Count == 0 ? "none" : string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))),
            [IvUnit] = units.Count == 0 ? Unavailable : string.Join(',', units),
        };
    }
}
