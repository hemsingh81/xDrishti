namespace XDrishti.Domain.Market.Strength;

/// <summary>
/// The baskets a <c>mom_rank</c> may be taken within (BK-09, pre-registered: a free choice would be a grid search by accident). The number is the code the feature carries as its first parameter and the stored row's basket is found by the name.
/// The names are those of the system baskets (BK-01 to BK-05) and of the derived liquid basket; a name outside this list is refused at compile time.
/// </summary>
public static class StrengthBaskets
{
    /// <summary>The names in code order: code 1 is the first.</summary>
    public static IReadOnlyList<string> Names { get; } = ["Nifty 50", "Nifty 100", "F&O stocks", "Most liquid"];

    /// <summary>The codes 1 to names.</summary>
    public static IReadOnlyList<double> Codes { get; } = [.. Enumerable.Range(1, Names.Count).Select(i => (double)i)];

    /// <summary>The code of a name (case-insensitive); false when it is not pre-registered.</summary>
    public static bool TryCode(string name, out int code)
    {
        for (var i = 0; i < Names.Count; i++)
        {
            if (string.Equals(Names[i], name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                code = i + 1;
                return true;
            }
        }

        code = 0;
        return false;
    }

    /// <summary>The name of a code; null when it is none.</summary>
    public static string? NameOf(int code) => code >= 1 && code <= Names.Count ? Names[code - 1] : null;
}
