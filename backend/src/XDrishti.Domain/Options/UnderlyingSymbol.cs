namespace XDrishti.Domain.Options;

/// <summary>The canonical symbol of an option underlying as a person types it: trimmed, upper case, <c>NSE:</c> added when no exchange is given (shared by the coverage route and the spread-model verb).</summary>
public static class UnderlyingSymbol
{
    private const int MaxLength = 60;

    /// <summary>The canonical symbol, or null for an empty or over-long text.</summary>
    public static string? Normalize(string? underlying)
    {
        var text = underlying?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return null;
        }

        return text.Contains(':', StringComparison.Ordinal) ? text : "NSE:" + text;
    }
}
