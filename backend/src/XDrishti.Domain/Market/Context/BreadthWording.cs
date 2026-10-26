namespace XDrishti.Domain.Market.Context;

/// <summary>The one wording of what the breadth is (P3-01e): the breadth of the owner's own tracked set, never of the market. The coverage shows <see cref="Label"/>, the plan carries <see cref="Note"/>.</summary>
public static class BreadthWording
{
    public static string Label(int? universe) =>
        universe is { } n
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"breadth of your {n} tracked shares (members as of today), not of the market")
            : "breadth of your tracked shares (members as of today), not of the market";

    public static string Note(int? universe) => Capitalised(Label(universe)) + ".";

    private static string Capitalised(string text) => string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));
}
