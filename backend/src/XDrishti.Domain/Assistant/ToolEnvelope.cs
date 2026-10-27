using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XDrishti.Domain.Assistant;

/// <summary>
/// The one answer shape of every tool (docs/specs/P9-00, spec P9-02): <c>{ok, tool, asOf, rows, data, rowCount, totalRows, truncated, evidence, notes, error, redactions, injectionFlags}</c>. Rows are written as objects keyed by <see cref="Columns"/>. The JSON is the
/// text the model reads and the text that is stored, so the same fields are read by the grounding check, the screen and the audit. <paramref name="OmitNullCells"/> leaves a null cell out of its row (for a catalog whose optional note would otherwise cost characters); a query result never uses it.
/// </summary>
public sealed record ToolEnvelope(
    bool Ok,
    string Tool,
    DateTimeOffset AsOf,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>>? Rows,
    JsonObject? Data,
    int? TotalRows,
    bool Truncated,
    ToolEvidence? Evidence,
    IReadOnlyList<string> Notes,
    ToolError? Error,
    int Redactions = 0,
    int InjectionFlags = 0,
    bool OmitNullCells = false)
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

    public int RowCount => Rows?.Count ?? 0;

    /// <summary>A failed call: no rows, the error code and a fixed message.</summary>
    public static ToolEnvelope Failure(string tool, DateTimeOffset asOf, string code, string message, IReadOnlyList<string>? notes = null) =>
        new(false, tool, asOf, [], null, null, null, false, null, notes ?? [], new ToolError(code, message));

    /// <summary>The JSON text of the envelope.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            Write(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// The envelope cut to <see cref="ToolLimits.MaxModelChars"/> characters of text and <see cref="ToolLimits.MaxEnvelopeBytes"/> bytes: rows are dropped from the end until it fits and <see cref="Truncated"/> is set (<see cref="TotalRows"/> keeps the number known before the cut).
    /// Returns the envelope and its JSON; when even an envelope without rows is too long, the rows and the data are dropped and the result is a failure with <see cref="ToolErrorCodes.TooLarge"/>.
    /// </summary>
    public (ToolEnvelope Envelope, string Json) Fit()
    {
        var current = this;
        var json = current.ToJson();
        if (Fits(json) || Rows is null)
        {
            return Fits(json) ? (current, json) : TooLarge();
        }

        var total = TotalRows ?? Rows.Count;
        var low = 0;
        var high = Rows.Count - 1;
        ToolEnvelope? best = null;
        string? bestJson = null;

        // The largest number of leading rows that fits (the text grows with the rows, so a binary search is enough).
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var candidate = this with { Rows = [.. Rows.Take(mid)], TotalRows = total, Truncated = true };
            var candidateJson = candidate.ToJson();
            if (Fits(candidateJson))
            {
                best = candidate;
                bestJson = candidateJson;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best is null ? TooLarge() : (best, bestJson!);

        (ToolEnvelope, string) TooLarge()
        {
            var failure = Failure(Tool, AsOf, ToolErrorCodes.TooLarge, "The result is too large to return; ask for fewer columns or rows.");
            return (failure, failure.ToJson());
        }
    }

    private static bool Fits(string json) => json.Length <= ToolLimits.MaxModelChars && System.Text.Encoding.UTF8.GetByteCount(json) <= ToolLimits.MaxEnvelopeBytes;

    private void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("ok", Ok);
        writer.WriteString("tool", Tool);
        writer.WriteString("asOf", AsOf.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        if (Rows is null)
        {
            writer.WriteNull("rows");
        }
        else
        {
            writer.WriteStartArray("rows");
            foreach (var row in Rows)
            {
                writer.WriteStartObject();
                for (var i = 0; i < Columns.Count && i < row.Count; i++)
                {
                    if (OmitNullCells && row[i] is null)
                    {
                        continue;
                    }

                    writer.WritePropertyName(Columns[i]);
                    WriteValue(writer, row[i]);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (Data is null)
        {
            writer.WriteNull("data");
        }
        else
        {
            writer.WritePropertyName("data");
            Data.WriteTo(writer);
        }

        writer.WriteNumber("rowCount", RowCount);
        if (TotalRows is { } total)
        {
            writer.WriteNumber("totalRows", total);
        }
        else
        {
            writer.WriteNull("totalRows");
        }

        writer.WriteBoolean("truncated", Truncated);
        WriteEvidence(writer);
        writer.WriteStartArray("notes");
        foreach (var note in Notes)
        {
            writer.WriteStringValue(note);
        }

        writer.WriteEndArray();
        if (Error is null)
        {
            writer.WriteNull("error");
        }
        else
        {
            writer.WriteStartObject("error");
            writer.WriteString("code", Error.Code);
            writer.WriteString("message", Error.Message);
            writer.WriteEndObject();
        }

        writer.WriteNumber("redactions", Redactions);
        writer.WriteNumber("injectionFlags", InjectionFlags);
        writer.WriteEndObject();
    }

    private void WriteEvidence(Utf8JsonWriter writer)
    {
        if (Evidence is null)
        {
            writer.WriteNull("evidence");
            return;
        }

        writer.WriteStartObject("evidence");
        writer.WriteString("basis", Evidence.Basis);
        writer.WriteStartArray("population");
        foreach (var item in Evidence.Population)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
        writer.WriteString("dateRange", Evidence.DateRange);
        if (Evidence.N is { } n)
        {
            writer.WriteNumber("n", n);
        }
        else
        {
            writer.WriteNull("n");
        }

        writer.WriteStartArray("limits");
        foreach (var item in Evidence.Limits)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("unavailable");
        foreach (var item in Evidence.Unavailable)
        {
            writer.WriteStartObject();
            writer.WriteString("input", item.Input);
            writer.WriteString("reason", item.Reason);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (Evidence.SqlHash is not null)
        {
            writer.WriteString("sqlHash", Evidence.SqlHash);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case int or long or short or byte or sbyte or uint or ulong or ushort:
                writer.WriteRawValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                break;
            case double d when double.IsFinite(d):
                writer.WriteNumberValue(d);
                break;
            case float f when float.IsFinite(f):
                writer.WriteNumberValue(f);
                break;
            case double or float:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case decimal m:
                writer.WriteStringValue(m.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonNode node:
                node.WriteTo(writer);
                break;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
