using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.SimExport.Manifest;

/// <summary>
/// <c>manifest.json</c> (spec P3-04b1, E6): <c>{ "format", "run_key", "basket_id" (or null), "export_key", "files": [ { "name", "bytes", "sha256" }… ], "extra": [ "…"… ] }</c>, files in ordinal name order, compact, no time. A manifest lists the
/// other files, never itself. It works over a list of entries and a list of extra lines, so a later slice adds files and lines without changing a type here. <see cref="Parse"/> is what the folder writer uses to decide that an export is
/// already there.
/// </summary>
public static class ExportManifest
{
    /// <summary>The code of a manifest that is not valid JSON of the shape above, has a wrong value, or states an export key that is not the key of its own content.</summary>
    public const string MalformedCode = "manifest.malformed";

    /// <summary>The code of a manifest with a file name that is not valid or appears twice.</summary>
    public const string NamesInvalidCode = "manifest.names_invalid";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, Encoder = JavaScriptEncoder.Default };

    /// <summary>The bytes of <c>manifest.json</c>.</summary>
    /// <exception cref="ArgumentException">An argument is not valid (see <see cref="ExportKey.Of"/>).</exception>
    public static byte[] Build(string format, string runKey, Guid? basketId, IReadOnlyList<ManifestEntry> entries, IReadOnlyList<string> extraLines)
    {
        var key = ExportKey.Of(format, runKey, basketId, entries, extraLines);

        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", format);
            writer.WriteString("run_key", runKey);
            if (basketId is { } basket)
            {
                writer.WriteString("basket_id", basket);
            }
            else
            {
                writer.WriteNull("basket_id");
            }

            writer.WriteString("export_key", key);
            writer.WriteStartArray("files");
            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteNumber("bytes", entry.Bytes);
                writer.WriteString("sha256", entry.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("extra");
            foreach (var line in extraLines)
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads a manifest. Strict: exactly the members above, no other, with values of the right kind, and the stated export key must be the key of the content.</summary>
    public static Result<ParsedManifest> Parse(ReadOnlySpan<byte> manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(manifest.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            return ParseRoot(document.RootElement);
        }
        catch (JsonException)
        {
            return Malformed("The manifest is not valid JSON.");
        }
    }

    private static Result<ParsedManifest> ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6
            || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("run_key", out var runKey) || runKey.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("basket_id", out var basket)
            || !root.TryGetProperty("export_key", out var exportKey) || exportKey.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("extra", out var extra) || extra.ValueKind != JsonValueKind.Array)
        {
            return Malformed("The manifest does not have exactly format, run_key, basket_id, export_key, files and extra.");
        }

        if (string.IsNullOrEmpty(format.GetString()) || !ExportText.IsLowerHex(runKey.GetString(), ExportText.Sha256HexLength) || !ExportText.IsLowerHex(exportKey.GetString(), ExportText.Sha256HexLength))
        {
            return Malformed("The format, the run key or the export key is not valid.");
        }

        Guid? basketId = null;
        if (basket.ValueKind == JsonValueKind.String)
        {
            if (!Guid.TryParseExact(basket.GetString(), "D", out var parsed))
            {
                return Malformed("The basket id is not a GUID.");
            }

            basketId = parsed;
        }
        else if (basket.ValueKind != JsonValueKind.Null)
        {
            return Malformed("The basket id is neither a GUID nor null.");
        }

        var entries = new List<ManifestEntry>();
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object || file.EnumerateObject().Count() != 3
                || !file.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !file.TryGetProperty("bytes", out var bytes) || bytes.ValueKind != JsonValueKind.Number || !bytes.TryGetInt64(out var length) || length < 0
                || !file.TryGetProperty("sha256", out var hash) || hash.ValueKind != JsonValueKind.String || !ExportText.IsLowerHex(hash.GetString(), ExportText.Sha256HexLength))
            {
                return Malformed("A file entry does not have a name, a length and a hash of the right kind.");
            }

            entries.Add(new ManifestEntry(name.GetString()!, length, hash.GetString()!));
        }

        var lines = new List<string>();
        foreach (var line in extra.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.String || ExportKey.HasLineBreak(line.GetString()!))
            {
                return Malformed("An extra line is not a text without a line break.");
            }

            lines.Add(line.GetString()!);
        }

        if (ExportNames.Problem(entries) is { } problem)
        {
            return Result.Failure<ParsedManifest>(Error.Validation(NamesInvalidCode, problem));
        }

        if (!string.Equals(ExportKey.Of(format.GetString()!, runKey.GetString()!, basketId, entries, lines), exportKey.GetString(), StringComparison.Ordinal))
        {
            return Malformed("The export key is not the key of the content: the manifest was edited.");
        }

        return Result.Success(new ParsedManifest(format.GetString()!, runKey.GetString()!, basketId, exportKey.GetString()!, entries.AsReadOnly(), lines.AsReadOnly()));
    }

    private static Result<ParsedManifest> Malformed(string message) => Result.Failure<ParsedManifest>(Error.Validation(MalformedCode, message));
}
