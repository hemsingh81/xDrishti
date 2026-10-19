using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// The rule of a trade as the card prints it (spec P3-04c, C3): read from <see cref="StrategyRecord.DefinitionJson"/> with <see cref="JsonDocument"/>, tolerantly (an unknown member is ignored, a missing one is skipped), for the side of the
/// card. The lines, in this order and each left out when its member is absent: <c>strategy</c> (id and effective parameters in name order), <c>setup</c>, <c>trigger</c>, <c>invalidate</c> (<c>&lt;mode&gt; of:</c> and each item on its own
/// indented line), <c>entry</c> (the kind), <c>stop</c> (the kind and each non-null member as <c>name = value</c> in the JSON order), one <c>exits</c> line per exit, one <c>overlays</c> line per overlay, <c>time rules</c> and one
/// <c>filters</c> line each. If the text does not parse or has no block for the side, <see cref="IsFallback"/> is true and the raw text is to be printed with its SHA-256. The text is the interim canonical one of P3-04 (until P3-02c): a change of it
/// breaks the golden of this view on purpose.
/// </summary>
public sealed record RuleView(IReadOnlyList<string> Lines, bool IsFallback, string Raw, string Sha256)
{
    /// <summary>The most characters of a raw definition a card prints.</summary>
    public const int MaxRawChars = 8000;

    /// <summary>The view of the rule for the side.</summary>
    public static RuleView Of(StrategyRecord strategy, Side side)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        var raw = strategy.DefinitionJson ?? string.Empty;
        var lines = TryRead(strategy, raw, side);
        return lines is null
            ? new RuleView([], true, raw.Length > MaxRawChars ? raw[..MaxRawChars] : raw, strategy.DefinitionSha256)
            : new RuleView(lines, false, string.Empty, strategy.DefinitionSha256);
    }

    private static List<string>? TryRead(StrategyRecord strategy, string raw, Side side)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(side == Side.Buy ? "buy" : "sell", out var block) || block.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var lines = new List<string> { "strategy: " + strategy.Id + Parameters(strategy.Parameters) };
            ConditionLines(lines, block, "setup");
            ConditionLines(lines, block, "trigger");
            ConditionLines(lines, block, "invalidate");
            if (Member(block, "entry", JsonValueKind.Object) is { } entry && Member(entry, "kind", JsonValueKind.String) is { } kind)
            {
                lines.Add("entry: " + kind.GetString());
            }

            if (Member(block, "stop", JsonValueKind.Object) is { } stop)
            {
                lines.Add(StopLine(stop));
            }

            NamedLines(lines, block, "exits", "policy");
            NamedLines(lines, block, "overlays", "kind");
            if (Member(root, "time_rules", JsonValueKind.Object) is { } rules)
            {
                TimeRules(lines, rules);
            }

            if (Member(root, "filters", JsonValueKind.Array) is { } filters)
            {
                lines.AddRange(filters.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.String).Select(f => "filters: " + f.GetString()));
            }

            return lines;
        }
        catch (Exception bad) when (bad is JsonException or InvalidOperationException)
        {
            // JsonException: not JSON; InvalidOperationException: a string with a lone surrogate cannot be read
            return null;
        }
    }

    private static string Parameters(IReadOnlyDictionary<string, double> parameters) =>
        parameters.Count == 0 ? string.Empty : " (" + string.Join(", ", parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " = " + CanonicalText.Number(p.Value))) + ")";

    private static void ConditionLines(List<string> lines, JsonElement block, string name)
    {
        if (Member(block, name, JsonValueKind.Object) is not { } list)
        {
            return;
        }

        var mode = Member(list, "mode", JsonValueKind.String)?.GetString() ?? "all";
        lines.Add(name + ": " + mode + " of:");
        if (Member(list, "items", JsonValueKind.Array) is { } items)
        {
            lines.AddRange(items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => "  " + i.GetString()));
        }
    }

    private static string StopLine(JsonElement stop)
    {
        var kind = Member(stop, "kind", JsonValueKind.String)?.GetString() ?? "?";
        var members = stop.EnumerateObject().Where(p => p.Name != "kind" && p.Value.ValueKind != JsonValueKind.Null).Select(p => p.Name + " = " + Scalar(p.Value)).ToList();
        return "stop: " + kind + (members.Count == 0 ? string.Empty : " (" + string.Join(", ", members) + ")");
    }

    private static void NamedLines(List<string> lines, JsonElement block, string name, string nameMember)
    {
        if (Member(block, name, JsonValueKind.Array) is not { } list)
        {
            return;
        }

        foreach (var item in list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
        {
            var title = Member(item, nameMember, JsonValueKind.String)?.GetString() ?? "?";
            var parameters = new List<string>();
            if (Member(item, "parameters", JsonValueKind.Array) is { } array)
            {
                foreach (var parameter in array.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
                {
                    var values = Member(parameter, "values", JsonValueKind.Array) is { } v ? string.Join(", ", v.EnumerateArray().Select(Scalar)) : string.Empty;
                    parameters.Add((Member(parameter, "name", JsonValueKind.String)?.GetString() ?? "?") + " = " + values);
                }
            }

            lines.Add(name + ": " + title + (parameters.Count == 0 ? string.Empty : " (" + string.Join("; ", parameters) + ")"));
        }
    }

    private static void TimeRules(List<string> lines, JsonElement rules)
    {
        var parts = new List<string>();
        foreach (var (member, label) in new[] { ("first_entry", "first entry"), ("last_entry", "last entry"), ("square_off", "square off"), ("max_hold_sessions", "max hold sessions") })
        {
            if (rules.TryGetProperty(member, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                parts.Add(label + " " + Scalar(value));
            }
        }

        if (parts.Count > 0)
        {
            lines.Add("time rules: " + string.Join(", ", parts));
        }
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.TryGetDouble(out var number) ? CanonicalText.Number(number) : value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => string.Create(CultureInfo.InvariantCulture, $"({value.ValueKind.ToString().ToLowerInvariant()})"),
    };

    private static JsonElement? Member(JsonElement parent, string name, JsonValueKind kind) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : null;
}
