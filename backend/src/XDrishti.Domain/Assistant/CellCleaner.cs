using System.Collections;
using System.Text.Json.Nodes;

namespace XDrishti.Domain.Assistant;

/// <summary>Counts what the cleaning of a result changed (the <c>redactions</c> and <c>injectionFlags</c> of the envelope).</summary>
public sealed class CleanCounters
{
    public int Redactions { get; private set; }

    public int InjectionFlags { get; private set; }

    internal void Add(int redactions, int flags)
    {
        Redactions += redactions;
        InjectionFlags += flags;
    }
}

/// <summary>
/// The one place where a result cell is made ready for the model (spec P9-02 AC 6 and 7). A string in a text column is cleaned as untrusted text, scrubbed, and cut to <see cref="ToolLimits.MaxCellChars"/>; a string in any other column is only cut; numbers, flags and other values are
/// untouched. Arrays are handled element by element.
/// </summary>
public static class CellCleaner
{
    public static object? Clean(object? value, bool isText, CleanCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        switch (value)
        {
            case string text when isText && IsPlainValue(text):
                return ToolLimits.CutCell(text);
            case string text when isText:
                var cleaned = UntrustedText.Clean(text);
                var scrubbed = OutputScrubber.Scrub(cleaned.Text, out var redactions);
                counters.Add(redactions, cleaned.InjectionFlags);
                return ToolLimits.CutCell(scrubbed);
            case string text:
                return ToolLimits.CutCell(text);
            case JsonNode node:
                return CleanNode(node, isText, counters);
            case IEnumerable items and not string:
                var list = new List<object?>();
                foreach (var item in items)
                {
                    list.Add(Clean(item, isText, counters));
                }

                return list;
            default:
                return value;
        }
    }

    /// <summary>
    /// A string that is only a decimal number with a decimal point (an amount or a level written as text), a GUID or an ISO date or moment is a value, not prose: the digit-run and long-token redaction would corrupt it and there is nothing in it to flag. A bare long integer is not exempt
    /// (it looks like an account number).
    /// </summary>
    private static bool IsPlainValue(string text) =>
        text.Length <= 64
        && ((text.Contains('.', StringComparison.Ordinal) && decimal.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out _))
            || Guid.TryParseExact(text, "D", out _)
            || (text.Length >= 10 && char.IsAsciiDigit(text[0]) && DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ssK"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)));

    /// <summary>A JSON value inside a cell (a metric, a level set): its strings are treated like cells, its numbers and flags are untouched. The node is copied, never changed.</summary>
    private static JsonNode? CleanNode(JsonNode node, bool isText, CleanCounters counters)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, child) in obj)
                {
                    copy[key] = child is null ? null : CleanNode(child, isText, counters);
                }

                return copy;
            case JsonArray array:
                return new JsonArray([.. array.Select(child => child is null ? null : CleanNode(child, isText, counters))]);
            case JsonValue leaf when leaf.TryGetValue<string>(out var text):
                return JsonValue.Create((string?)Clean(text, isText, counters));
            default:
                return node.DeepClone();
        }
    }
}
