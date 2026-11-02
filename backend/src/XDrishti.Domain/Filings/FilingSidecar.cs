using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The small JSON file beside a filing that says what it is and when the exchange broadcast it (P15-01). Schema 1, UTF-8, at most 4 KB, no unknown and no duplicate keys. <see cref="Parse"/> never throws on bad input: the first problem is returned as a closed
/// code (<c>sidecar_too_large</c>, <c>invalid_json</c>, <c>not_an_object</c>, <c>duplicate_key</c>, <c>unknown_key</c>, <c>bad_schema</c>, <c>bad_symbol</c>, <c>bad_kind</c>, <c>bad_period_end</c>, <c>bad_source_ref</c>). The code never repeats the file's text.
/// </summary>
public sealed partial record FilingSidecar(string Symbol, FilingKind Kind, DateOnly? PeriodEnd, BroadcastTime Broadcast, string? SourceRef)
{
    public const int MaxSourceRefLength = 200;

    public const string WildcardSymbol = "*";

    private static readonly HashSet<string> Keys = ["schema", "symbol", "kind", "period_end", "broadcast_ts", "source_ref"];

    [GeneratedRegex("^[A-Z0-9&-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();

    /// <summary>Whether a filing of this kind in a file of this extension carries its own times per row (a csv of board meetings, shareholding or announcements, P15-04 and P15-07). An announcement as a text or pdf file is an ordinary envelope.</summary>
    public static bool IsRowLevel(FilingKind kind, string extension) =>
        extension == ".csv" && kind is FilingKind.BoardMeeting or FilingKind.Shareholding or FilingKind.Announcement;

    public static Result<FilingSidecar> Parse(ReadOnlySpan<byte> bytes, string extension, DateTimeOffset now)
    {
        if (bytes.Length > FilingNames.MaxSidecarBytes)
        {
            return Fail("sidecar_too_large");
        }

        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            bytes = bytes[3..];
        }

        var values = new Dictionary<string, (JsonTokenType Type, string? Text)>(StringComparer.Ordinal);
        string? problem;
        try
        {
            problem = Read(bytes, values);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Fail("invalid_json");
        }

        if (problem is not null)
        {
            return Fail(problem);
        }

        return Validate(values, extension, now);
    }

    private static string? Read(ReadOnlySpan<byte> bytes, Dictionary<string, (JsonTokenType Type, string? Text)> values)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 4 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return "not_an_object";
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString()!;
            if (!reader.Read())
            {
                return "invalid_json";
            }

            if (values.ContainsKey(name))
            {
                return "duplicate_key";
            }

            if (!Keys.Contains(name))
            {
                return "unknown_key";
            }

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
                values[name] = (JsonTokenType.StartObject, null);
                continue;
            }

            values[name] = (reader.TokenType, reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
                _ => null,
            });
        }

        // The loop ended on EndObject (or the reader threw); anything after the object is not allowed.
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            return "invalid_json";
        }

        return null;
    }

    private static Result<FilingSidecar> Validate(Dictionary<string, (JsonTokenType Type, string? Text)> values, string extension, DateTimeOffset now)
    {
        if (!values.TryGetValue("schema", out var schema) || schema.Type != JsonTokenType.Number || schema.Text != "1")
        {
            return Fail("bad_schema");
        }

        if (!values.TryGetValue("kind", out var kindValue) || kindValue.Type != JsonTokenType.String || !FilingKinds.TryParse(kindValue.Text, out var kind))
        {
            return Fail("bad_kind");
        }

        var rowLevel = IsRowLevel(kind, extension);
        if (!values.TryGetValue("symbol", out var symbolValue) || symbolValue.Type != JsonTokenType.String || symbolValue.Text is null
            || !(SymbolPattern().IsMatch(symbolValue.Text) || (rowLevel && symbolValue.Text == WildcardSymbol)))
        {
            return Fail("bad_symbol");
        }

        DateOnly? periodEnd = null;
        if (values.TryGetValue("period_end", out var periodValue) && periodValue.Type != JsonTokenType.Null)
        {
            if (periodValue.Type != JsonTokenType.String || !DateOnly.TryParseExact(periodValue.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return Fail("bad_period_end");
            }

            periodEnd = parsed;
        }

        var needsPeriod = kind == FilingKind.Results || (kind == FilingKind.Shareholding && !rowLevel);
        if (needsPeriod && periodEnd is null)
        {
            return Fail("bad_period_end");
        }

        string? sourceRef = null;
        if (values.TryGetValue("source_ref", out var refValue) && refValue.Type != JsonTokenType.Null)
        {
            if (refValue.Type != JsonTokenType.String || refValue.Text is null || refValue.Text.Length > MaxSourceRefLength)
            {
                return Fail("bad_source_ref");
            }

            sourceRef = refValue.Text;
        }

        var raw = values.TryGetValue("broadcast_ts", out var broadcastValue) && broadcastValue.Type == JsonTokenType.String ? broadcastValue.Text : null;
        var broadcast = BroadcastRule.Evaluate(raw, now);
        if (broadcastValue.Type is JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.StartObject)
        {
            broadcast = new BroadcastTime(null, BroadcastState.Unavailable, BroadcastRule.Unparsable);
        }

        if (rowLevel)
        {
            broadcast = new BroadcastTime(broadcast.Instant, BroadcastState.RowLevelTs, null);
        }

        return Result.Success(new FilingSidecar(symbolValue.Text, kind, periodEnd, broadcast, sourceRef));
    }

    private static Result<FilingSidecar> Fail(string code) => Result.Failure<FilingSidecar>(Error.Validation(code, code));
}
