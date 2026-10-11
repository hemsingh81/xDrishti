namespace XDrishti.Infrastructure.Accounts;

internal static class LikePattern
{
    /// <summary>Escapes the characters that mean something in a LIKE/ILIKE pattern (<c>\</c>, <c>%</c>, <c>_</c>), so the text only matches itself.</summary>
    public static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
