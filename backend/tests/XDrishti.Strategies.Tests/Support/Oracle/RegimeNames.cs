namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>
/// The oracle's own table of the nine regime names (doc 07 §6: trend × volatility, the trend in the order up, range, down and the volatility in the order low, normal, high; the code is <c>(1 − trend) × 3 + vol + 1</c> with
/// the trend +1, 0, −1 and the volatility 0, 1, 2), built from the two lists and not read from the library.
/// </summary>
internal static class RegimeNames
{
    private static readonly string[] Trends = ["UP", "RANGE", "DOWN"];

    private static readonly string[] Volatilities = ["LOW", "NORMAL", "HIGH"];

    /// <summary>The names for the codes 1 to 9, in order.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Trends.SelectMany(t => Volatilities.Select(v => t + "_" + v + "_VOL"))];

    /// <summary>The code 1 to 9 of a name.</summary>
    public static int CodeOf(string name) => All.ToList().IndexOf(name) + 1;
}
