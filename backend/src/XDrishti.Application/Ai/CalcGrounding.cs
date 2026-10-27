using System.Globalization;
using System.Text.Json.Nodes;
using XDrishti.Domain.Assistant.Grounding;

namespace XDrishti.Application.Ai;

/// <summary>
/// Decides whether the result of <c>calc</c> may count as a source of figures (ADR 0139): only when it has at least one operand that is not a free literal and every such operand equals, exactly, a number of an earlier result of another tool of the same turn or of the owner's question. Each operand is
/// compared by its own value against the numbers of the sources, not through the figure filter of prose (which drops years). An expression made only of the free literals (0, 1, 100, 0.5) is never a source, nor is a result of <c>calc</c> a source for another <c>calc</c>. What becomes a source is
/// only the result and the operands, never an argument the model chose (such as <c>places</c>).
/// </summary>
public static class CalcGrounding
{
    /// <summary>The number literals that need no source: 0, 1, 100 and 0.5. An expression made only of them is not a source.</summary>
    public static readonly IReadOnlyList<string> Structural = ["0", "1", "100", "0.5"];

    /// <summary>The text that stands for the calc result as a source (result and operands only), or null when the result may not be a source.</summary>
    public static string? SourceText(string calcResultJson, IReadOnlyList<string> nonCalcSources, string question)
    {
        ArgumentNullException.ThrowIfNull(calcResultJson);
        ArgumentNullException.ThrowIfNull(nonCalcSources);
        try
        {
            if (JsonNode.Parse(calcResultJson) is not JsonObject root || root["ok"]?.GetValue<bool>() != true || root["data"] is not JsonObject data || data["operands"] is not JsonArray operands || data["result"]?.GetValue<string>() is not { } result)
            {
                return null;
            }

            var known = NumberTokenizer.Sources(question).Select(s => s.Value).ToHashSet();
            foreach (var text in nonCalcSources)
            {
                known.UnionWith(NumberTokenizer.Sources(text).Select(s => s.Value));
            }

            var texts = new List<string>();
            var constrained = 0;
            foreach (var node in operands)
            {
                var text = node?.GetValue<string>();
                if (text is null || !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                {
                    return null;
                }

                texts.Add(text);
                if (Structural.Contains(text, StringComparer.Ordinal) || Structural.Any(s => decimal.Parse(s, CultureInfo.InvariantCulture) == value))
                {
                    continue;
                }

                constrained++;
                if (!known.Contains(value) && !known.Contains(value * 100m))
                {
                    return null;
                }
            }

            return constrained == 0 ? null : "{\"result\":\"" + result + "\",\"operands\":[" + string.Join(",", texts.Select(t => "\"" + t + "\"")) + "]}";
        }
        catch (Exception failure) when (failure is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static bool IsGrounded(string calcResultJson, IReadOnlyList<string> nonCalcSources, string question) => SourceText(calcResultJson, nonCalcSources, question) is not null;
}
