using System.Text.RegularExpressions;

namespace XDrishti.Application.Learning;

/// <summary>A stored run whose closed trades make cells, with the labels the run does not carry itself: the style and the exit policy (the verb names them, as for an exit choice).</summary>
/// <param name="RunKey">The 64-hex key of a stored run.</param>
/// <param name="Style"><c>intraday</c> or <c>swing</c>.</param>
/// <param name="Exit">The label of the exit policy the run used.</param>
public sealed partial record CellSource(string RunKey, string Style, string Exit)
{
    /// <summary>Reads one <c>KEY:STYLE:EXIT</c>, the exit label being the rest (it may hold ':' or ','); null when it has fewer than three parts.</summary>
    public static CellSource? TryParse(string? text)
    {
        var parts = text?.Split(':', 3);
        return parts is { Length: 3 } ? new CellSource(parts[0], parts[1], parts[2]) : null;
    }

    /// <summary>
    /// Reads the comma-joined sources text of a stored snapshot. An exit label may hold ',' or ':', so a piece starts a new source only where it looks like <c>KEY:intraday:</c> or <c>KEY:swing:</c>; any other piece belongs to the label before it.
    /// Null when the text is empty or a source is malformed.
    /// </summary>
    public static List<CellSource>? TryParseList(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var joined = new List<string>();
        foreach (var piece in text.Split(','))
        {
            if (joined.Count == 0 || StartsSource().IsMatch(piece))
            {
                joined.Add(piece);
            }
            else
            {
                joined[^1] += "," + piece;
            }
        }

        var result = new List<CellSource>(joined.Count);
        foreach (var item in joined)
        {
            if (TryParse(item) is not { } source)
            {
                return null;
            }

            result.Add(source);
        }

        return result;
    }

    [GeneratedRegex("^[^:,]+:(intraday|swing):")]
    private static partial Regex StartsSource();
}
