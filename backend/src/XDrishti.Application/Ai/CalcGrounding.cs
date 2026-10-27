using System.Text.Json.Nodes;
using XDrishti.Domain.Assistant.Grounding;

namespace XDrishti.Application.Ai;

/// <summary>
/// Decides whether the result of <c>calc</c> may count as a source of figures (ADR 0139): only when every number the expression held (echoed in <c>data.operands</c>) is found in an earlier result of another tool of the same turn, or in the owner's question. Otherwise the model could invent a figure and
/// launder it through <c>calc("0.9+0")</c>. A result of <c>calc</c> is never a source for another <c>calc</c>. The few structural literals in <see cref="Structural"/> are allowed (a percent factor, a count of one, a half).
/// </summary>
public static class CalcGrounding
{
    /// <summary>The number literals that need no source: 0, 1, 100 and 0.5.</summary>
    public static readonly IReadOnlyList<string> Structural = ["0", "1", "100", "0.5"];

    public static bool IsGrounded(string calcResultJson, IReadOnlyList<string> nonCalcSources, string question)
    {
        ArgumentNullException.ThrowIfNull(calcResultJson);
        ArgumentNullException.ThrowIfNull(nonCalcSources);
        try
        {
            if (JsonNode.Parse(calcResultJson) is not JsonObject root || root["ok"]?.GetValue<bool>() != true || root["data"]?["operands"] is not JsonArray operands)
            {
                return false;
            }

            foreach (var operand in operands.Select(o => o?.GetValue<string>()))
            {
                if (operand is null)
                {
                    return false;
                }

                if (Structural.Contains(operand, StringComparer.Ordinal))
                {
                    continue;
                }

                if (GroundingCheck.Check(operand, question, nonCalcSources).Ungrounded.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception failure) when (failure is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
