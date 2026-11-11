using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Json;

/// <summary>
/// <c>run.json</c> (spec P3-04b1, E4): <c>{ "export": { "format", "schema" }, "labels": …, "run": … }</c>, the full record of the run in compact JSON for machines (a tool such as <c>jq</c> reads it; the CSV and the cards are for eyes).
/// Strings are escaped by the framework's default encoder (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> and non-ASCII characters as <c>\uXXXX</c>), so the file is safe wherever it is pasted. Streams to a sink the host owns and keeps no copy of the file. The file holds no clock time, no machine path and no culture-dependent text; dictionaries are written with ordinal sorted keys; a NaN or an infinity throws.
/// </summary>
public static class SimExportJson
{
    /// <summary>The schema of the envelope (<c>export.schema</c>).</summary>
    public const int Schema = 1;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.Default,
        SkipValidation = false,
    };

    private static readonly string[] TradeMembers = ["trades", "open_at_end"];

    private static readonly JsonSerializerOptions RecordOptions = new(SimExportJsonContext.Default.Options)
    {
        TypeInfoResolver = SimExportJsonContext.Default.WithAddedModifier(WithoutTrades).WithAddedModifier(WithoutZeroTierExcluded).WithAddedModifier(WithoutTrainingFlag),
    };

    /// <summary>The export options: the context's, with the counter of CF-05 left out while it is 0.</summary>
    private static readonly JsonSerializerOptions ExportOptions = new(SimExportJsonContext.Default.Options)
    {
        TypeInfoResolver = SimExportJsonContext.Default.WithAddedModifier(WithoutZeroTierExcluded).WithAddedModifier(WithoutTrainingFlag),
    };

    /// <summary>Writes <c>run.json</c> to the sink.</summary>
    /// <param name="sink">Where the UTF-8 bytes go; the host owns it.</param>
    /// <param name="run">The finished run.</param>
    /// <param name="labels">The labels of the run, from <see cref="RunLabels.Of"/>.</param>
    /// <param name="format">The format string of the export (<c>xd-sim-export-1</c>); a string the caller gives, so that no assembly version is written.</param>
    /// <exception cref="ArgumentException">A value is a NaN or an infinity, or the format is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An enum value has no spelling in <see cref="ExportSpellings"/>.</exception>
    public static void Write(IBufferWriter<byte> sink, SimulationRun run, RunLabelSet labels, string format)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var envelope = new RunEnvelope(new ExportHeader(format, Schema), new LabelsView(labels.Lines, labels.NotModelled, RunLabels.IsVerified(run.EngineVersion)), run);
        using var writer = new Utf8JsonWriter(sink, WriterOptions);
        JsonSerializer.Serialize(writer, envelope, (JsonTypeInfo<RunEnvelope>)ExportOptions.GetTypeInfo(typeof(RunEnvelope)));
        writer.Flush();
    }

    /// <summary>
    /// Writes the run object of <c>run.json</c> without its <c>trades</c> and <c>open_at_end</c> members (spec P3-10, D4): the record the result store keeps as text, in exactly the canonical form of the file (the same converters, the same order,
    /// compact, the same escaping), so that the store and the file agree on one text. No envelope, no labels. Deterministic.
    /// </summary>
    /// <param name="sink">Where the UTF-8 bytes go; the host owns it.</param>
    /// <param name="run">The finished run.</param>
    /// <exception cref="ArgumentException">A value is a NaN or an infinity.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An enum value has no spelling in <see cref="ExportSpellings"/>.</exception>
    public static void WriteRecord(IBufferWriter<byte> sink, SimulationRun run)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(run);

        using var writer = new Utf8JsonWriter(sink, WriterOptions);
        JsonSerializer.Serialize(writer, run, (JsonTypeInfo<SimulationRun>)RecordOptions.GetTypeInfo(typeof(SimulationRun)));
        writer.Flush();
    }

    /// <summary>
    /// CF-05 (ADR 0229): the <c>tier_excluded</c> counter of the fates and of the walk counters is written only when it is above 0, so the record text, and with it the record hash, of a run without conservative slippage is what it was before the counter existed.
    /// </summary>
    private static void WithoutZeroTierExcluded(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(ScanCounters))
        {
            // BK-09: the feature_unknown counter exists only for a run that reads the stored relative-strength features, so every other record text and hash is as before
            foreach (var property in typeInfo.Properties.Where(p => string.Equals(p.Name, "feature_unknown", StringComparison.Ordinal)))
            {
                property.ShouldSerialize = static (_, value) => value is not 0L;
            }

            return;
        }

        if (typeInfo.Type == typeof(SimulationRun))
        {
            // BK-11b: universe_basis exists only for a basket run that named a members basis, so every other record text and hash is as before
            foreach (var property in typeInfo.Properties.Where(p => string.Equals(p.Name, "relative_strength", StringComparison.Ordinal) || string.Equals(p.Name, "universe_basis", StringComparison.Ordinal)))
            {
                property.ShouldSerialize = static (_, value) => value is not null;
            }

            return;
        }

        if (typeInfo.Type != typeof(RunFates) && typeInfo.Type != typeof(WalkCounters))
        {
            return;
        }

        foreach (var property in typeInfo.Properties.Where(p => (string.Equals(p.Name, "tier_excluded", StringComparison.Ordinal) || string.Equals(p.Name, "turnover_blocked", StringComparison.Ordinal) || string.Equals(p.Name, "not_member", StringComparison.Ordinal))))
        {
            property.ShouldSerialize = static (_, value) => value is not 0L;
        }
    }

    /// <summary>
    /// AU-11 (ADR 0243): <c>IsTraining</c> says why a run exists and is carried to the store beside the record, never in it, so the record text and hash of every run are as before. Kept out here so the Domain type stays free of serializer attributes.
    /// </summary>
    private static void WithoutTrainingFlag(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(SimulationRun))
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (string.Equals(typeInfo.Properties[i].Name.Replace("_", string.Empty, StringComparison.Ordinal), "istraining", StringComparison.OrdinalIgnoreCase))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }

    private static void WithoutTrades(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(SimulationRun))
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (TradeMembers.Contains(typeInfo.Properties[i].Name, StringComparer.Ordinal))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}
