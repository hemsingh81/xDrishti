using System.Text.Json;

namespace XDrishti.Application.Ai;

/// <summary>Reads the facts the stream, the audit and the injection signal need from the JSON text of a tool result. Never throws; a text that is not an envelope gives <c>Parsed = false</c>.</summary>
public static class ToolResultReader
{
    public static ToolResultFacts Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Unparsed;
            }

            var ok = root.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
            string? code = null;
            string? message = null;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                code = Text(error, "code");
                message = Text(error, "message");
            }

            TurnEvidence? evidence = null;
            string? evidenceJson = null;
            if (root.TryGetProperty("evidence", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                evidenceJson = e.GetRawText();
                evidence = new TurnEvidence(Text(e, "basis"), e.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var count) ? count : null, Text(e, "dateRange"));
            }

            return new ToolResultFacts(true, ok, code, message, Int(root, "rowCount"), root.TryGetProperty("truncated", out var t) && t.ValueKind == JsonValueKind.True, evidenceJson, evidence, Int(root, "redactions"), Int(root, "injectionFlags"));
        }
        catch (JsonException)
        {
            return Unparsed;
        }
    }

    private static ToolResultFacts Unparsed { get; } = new(false, false, "tool.invalid_result", "The tool server returned text that is not a result.", 0, false, null, null, 0, 0);

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}
