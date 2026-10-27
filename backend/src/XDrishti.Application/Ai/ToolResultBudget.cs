namespace XDrishti.Application.Ai;

/// <summary>
/// The tool-result text one turn may send to the model (spec P9-03 AC 5): the sum is at most <c>budget</c> characters; a result that would pass it is cut to <c>cut</c> characters (and to what is left) and says so. One instance per turn.
/// </summary>
public sealed class ToolResultBudget(int budget, int cut)
{
    public const string TruncatedMark = "...[truncated]";

    private int _used;

    /// <summary>The characters handed out so far.</summary>
    public int Used => _used;

    /// <summary>The text to send for a result of this text, and whether it was cut.</summary>
    public (string Text, bool Cut) Fit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var left = budget - _used;
        if (text.Length <= left)
        {
            _used += text.Length;
            return (text, false);
        }

        var keep = Math.Max(0, Math.Min(cut, left) - TruncatedMark.Length);
        var shortened = text[..Math.Min(keep, text.Length)] + TruncatedMark;
        _used += shortened.Length;
        return (shortened, true);
    }
}
