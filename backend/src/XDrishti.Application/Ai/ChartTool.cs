using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Ai;

/// <summary>
/// <c>make_chart</c> (spec P9-05 AC 12), the one tool that runs inside the API because it needs this turn's stored results. It takes no number from the model: it checks that the columns it is told to draw exist in earlier, successful calls of this turn, that the y columns are numeric and the
/// lengths agree, and returns a spec that points at those calls. The screen draws from the stored rows of the cited calls. A bad reference is <c>chart.bad_reference</c> naming which. Pure over the texts it is given.
/// </summary>
public static class ChartTool
{
    public const string Name = "make_chart";
    public const string BadReference = "chart.bad_reference";
    public const int MaxPoints = 200;
    public const int MaxSeries = 4;
    public const int MaxTitle = 80;

    public static readonly ToolDescriptor Descriptor = new(
        Name,
        "Charts columns of earlier tool results of this question (bar, line, scatter). Give call numbers and column names; never numbers. At most 200 points and 4 series.",
        """{"type":"object","additionalProperties":false,"required":["kind","title","x","y"],"properties":{"kind":{"type":"string","enum":["bar","line","scatter"]},"title":{"type":"string","maxLength":80},"x":{"type":"object","required":["callIndex","column"],"properties":{"callIndex":{"type":"integer"},"column":{"type":"string"}}},"y":{"type":"array","maxItems":4,"items":{"type":"object","required":["callIndex","column","label"],"properties":{"callIndex":{"type":"integer"},"column":{"type":"string"},"label":{"type":"string"}}}}}}""");

    /// <summary>Checks the call against the results of the turn (by call number, the JSON text each returned) and answers with the envelope text; <paramref name="reason"/> is a one-word reason when it was refused.</summary>
    public static string Run(string argumentsJson, IReadOnlyDictionary<int, string> results, DateTimeOffset asOf, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(results);
        reason = null;
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Bad(asOf, "arguments", "the arguments are not a JSON object", out reason);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Bad(asOf, "arguments", "the arguments are not a JSON object", out reason);
        }

        var kind = Text(root, "kind");
        var title = Text(root, "title");
        if (kind is not ("bar" or "line" or "scatter"))
        {
            return Bad(asOf, "kind", "kind is bar, line or scatter", out reason);
        }

        if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitle || HasMarkup(title) || title.Any(char.IsDigit))
        {
            return Bad(asOf, "title", $"title is 1 to {MaxTitle} characters without markup or digits (a figure in a title would not come from a tool)", out reason);
        }

        if (!root.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Object || !TryRef(x, out var xCall, out var xColumn))
        {
            return Bad(asOf, "x", "x needs callIndex and column", out reason);
        }

        if (!root.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Array || y.GetArrayLength() is 0 or > MaxSeries)
        {
            return Bad(asOf, "series", $"y holds 1 to {MaxSeries} series", out reason);
        }

        var xRows = RowsOf(results, xCall, out var problem);
        if (xRows is null)
        {
            return Bad(asOf, "x", problem!, out reason);
        }

        if (!HasColumn(xRows, xColumn))
        {
            return Bad(asOf, "x", $"call {xCall.ToString(CultureInfo.InvariantCulture)} has no column '{Clip(xColumn)}'", out reason);
        }

        if (xRows.Count > MaxPoints)
        {
            return Bad(asOf, "points", $"more than {MaxPoints} points", out reason);
        }

        var series = new JsonArray();
        var index = 0;
        foreach (var item in y.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object || !TryRef(item, out var call, out var column) || Text(item, "label") is not { Length: > 0 and <= 40 } label || HasMarkup(label) || label.Any(char.IsDigit))
            {
                return Bad(asOf, "series", $"series {index.ToString(CultureInfo.InvariantCulture)} needs callIndex, column and a label of up to 40 characters without markup or digits", out reason);
            }

            var rows = RowsOf(results, call, out problem);
            if (rows is null)
            {
                return Bad(asOf, "series", problem!, out reason);
            }

            if (!HasColumn(rows, column))
            {
                return Bad(asOf, "series", $"call {call.ToString(CultureInfo.InvariantCulture)} has no column '{Clip(column)}'", out reason);
            }

            if (!rows.All(r => NumberOf(r[column]) is not null || r[column] is null || IsGap(r[column])))
            {
                return Bad(asOf, "series", $"column '{Clip(column)}' of call {call.ToString(CultureInfo.InvariantCulture)} is not numeric", out reason);
            }

            if (rows.Count != xRows.Count)
            {
                return Bad(asOf, "points", "the series do not have as many rows as x", out reason);
            }

            series.Add(new JsonObject { ["callIndex"] = call, ["column"] = column, ["label"] = label });
        }

        var envelope = new JsonObject
        {
            ["ok"] = true,
            ["tool"] = Name,
            ["asOf"] = asOf.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["rows"] = null,
            ["data"] = new JsonObject { ["kind"] = kind, ["title"] = title, ["x"] = new JsonObject { ["callIndex"] = xCall, ["column"] = xColumn }, ["series"] = series, ["points"] = xRows.Count },
            ["rowCount"] = 0,
            ["truncated"] = false,
            ["evidence"] = null,
            ["notes"] = new JsonArray("the chart draws the stored rows of the cited calls; it holds no number of its own"),
            ["error"] = null,
        };
        return envelope.ToJsonString();
    }

    private static string Bad(DateTimeOffset asOf, string which, string message, out string? reason)
    {
        reason = which;
        return new ToolEnvelope(false, Name, asOf, [], null, null, null, false, null, [], new ToolError(BadReference, $"{which}: {message}")).ToJson();
    }

    private static bool TryRef(JsonElement element, out int call, out string column)
    {
        call = 0;
        column = string.Empty;
        if (!element.TryGetProperty("callIndex", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out call) || Text(element, "column") is not { Length: > 0 and <= 64 } name)
        {
            return false;
        }

        column = name;
        return true;
    }

    private static List<JsonObject>? RowsOf(IReadOnlyDictionary<int, string> results, int call, out string? problem)
    {
        problem = null;
        if (!results.TryGetValue(call, out var text))
        {
            problem = $"call {call.ToString(CultureInfo.InvariantCulture)} is not a call of this question";
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject envelope && envelope["ok"]?.GetValue<bool>() == true && envelope["rows"] is JsonArray rows)
            {
                return [.. rows.OfType<JsonObject>()];
            }
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or FormatException)
        {
            // falls through to the same refusal: a result that cannot be read is not a result to draw
        }

        problem = $"call {call.ToString(CultureInfo.InvariantCulture)} failed or returned no rows";
        return null;
    }

    private static bool HasColumn(List<JsonObject> rows, string column) => rows.Count > 0 ? rows.Any(r => r.ContainsKey(column)) : false;

    /// <summary>A figure that is not a value (insufficient, unavailable ...) is a gap in the chart, never a digit.</summary>
    private static bool IsGap(JsonNode? node) => node is JsonObject o && o["state"] is JsonValue && o["number"] is null;

    private static double? NumberOf(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonObject o when o["number"] is JsonValue n && n.TryGetValue<double>(out var d) => d,
        _ => null,
    };

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool HasMarkup(string text) => text.Any(c => c is '<' or '>' or '&' || char.IsControl(c));

    private static string Clip(string text) => new([.. text.Where(c => !char.IsControl(c) && c is not ('<' or '>' or '&')).Take(24)]);
}
