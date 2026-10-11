namespace XDrishti.Infrastructure.Persistence;

/// <summary>Helpers for LIKE/ILIKE patterns built from user text, shared by the modules' stores.</summary>
internal static class LikePattern
{
    /// <summary>The escape character <see cref="Escape"/> uses; pass it to <c>EF.Functions.ILike(…, pattern, EscapeCharacter)</c> so the pattern is read the same way whatever the provider's default.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>Escapes the characters that mean something in a LIKE/ILIKE pattern (<c>\</c>, <c>%</c>, <c>_</c>), so the text only matches itself.</summary>
    public static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
