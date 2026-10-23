using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// Turns the exit label of a cell into the exit a replay is asked to use (P7-13, ADR 0090). Only a label spelt as a candidate, <c>FIXED_RR{rr=2}</c>, can be replayed: the label must round-trip through the exit flags to the same
/// spelling. A bare policy name, an overlay (<c>+time_stop</c>) or anything else is refused with a reason; the grade is then "not gradable", never a guess.
/// </summary>
public static class ExitLabel
{
    /// <summary>Reads the label; <paramref name="choice"/> is set when it can be replayed, otherwise <paramref name="reason"/> says why not.</summary>
    public static bool TryRead(string? label, [NotNullWhen(true)] out ExitChoice? choice, out string reason)
    {
        choice = null;
        var text = label?.Trim() ?? string.Empty;
        var open = text.IndexOf('{', StringComparison.Ordinal);
        if (text.Length == 0 || open <= 0 || text[^1] != '}' || open == text.Length - 2)
        {
            reason = "the exit label `" + Show(text) + "` does not name a candidate (policy and parameters, such as FIXED_RR{rr=2}), so the exit cannot be replayed";
            return false;
        }

        var body = text[(open + 1)..^1];
        var spec = text[..open] + (body.Length == 0 ? string.Empty : ":" + body);
        var errors = new List<string>();
        var parsed = ExitFlags.Parse([spec], null, errors);
        if (parsed is null || parsed.Candidates.Count != 1 || !string.Equals(parsed.Candidates[0].Id, text, StringComparison.Ordinal))
        {
            reason = "the exit label `" + Show(text) + "` is not a single exit candidate that reads back to the same spelling";
            return false;
        }

        choice = parsed;
        reason = string.Empty;
        return true;
    }

    private static string Show(string text) => text.Length <= 64 ? text : string.Create(CultureInfo.InvariantCulture, $"{text.AsSpan(0, 63)}…");
}
