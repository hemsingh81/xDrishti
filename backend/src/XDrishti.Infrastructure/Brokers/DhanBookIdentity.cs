namespace XDrishti.Infrastructure.Brokers;

/// <summary>The ISIN of a book row, when it has the shape of one (2 letters, 9 letters or digits, a check digit); upper case. Pure.</summary>
internal static class DhanBookIdentity
{
    public static string? Isin(string? text)
    {
        var code = text?.Trim().ToUpperInvariant();
        return code is { Length: 12 } && char.IsAsciiLetterUpper(code[0]) && char.IsAsciiLetterUpper(code[1]) && code[2..11].All(char.IsAsciiLetterOrDigit) && char.IsAsciiDigit(code[11]) ? code : null;
    }
}
