using System.Globalization;

namespace XDrishti.Domain.PairResearch;

/// <summary>One trading variant of protocol S: the entry level <c>e</c> (in z) and the look-back <c>L</c> in sessions of the z-score window. Each of the six variants of <see cref="Grid"/> is one trial; the pre-registered primary is <see cref="Primary"/> (e 2.0, L 5).</summary>
/// <param name="Entry">1.5, 2.0 or 2.5.</param>
/// <param name="Lookback">5 or 10 sessions.</param>
public sealed record PairVariant(double Entry, int Lookback)
{
    /// <summary>The six variants, entry level outer, look-back inner.</summary>
    public static IReadOnlyList<PairVariant> Grid { get; } = [new(1.5, 5), new(1.5, 10), new(2.0, 5), new(2.0, 10), new(2.5, 5), new(2.5, 10)];

    /// <summary>The pre-registered primary variant.</summary>
    public static PairVariant Primary { get; } = new(2.0, 5);

    /// <summary>The stored label, <c>e2.0/L5</c>.</summary>
    public string Label => string.Create(CultureInfo.InvariantCulture, $"e{Entry:0.0}/L{Lookback}");

    /// <summary>The variant of the grid with this label, or null.</summary>
    public static PairVariant? FromLabel(string? label) => Grid.FirstOrDefault(v => string.Equals(v.Label, label, StringComparison.Ordinal));

    /// <summary>Parses <c>2.0,5</c> (entry, look-back); null when it is not one of the grid.</summary>
    public static PairVariant? Parse(string? text)
    {
        var parts = text?.Split(',');
        if (parts is not { Length: 2 } || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var entry) || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lookback))
        {
            return null;
        }

        return Grid.FirstOrDefault(v => v.Entry == entry && v.Lookback == lookback);
    }
}
