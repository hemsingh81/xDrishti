using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
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

        var envelope = new RunEnvelope(new ExportHeader(format, Schema), new LabelsView(labels.Lines, labels.NotModelled, EngineVerified: false), run);
        using var writer = new Utf8JsonWriter(sink, WriterOptions);
        JsonSerializer.Serialize(writer, envelope, SimExportJsonContext.Default.RunEnvelope);
        writer.Flush();
    }
}
