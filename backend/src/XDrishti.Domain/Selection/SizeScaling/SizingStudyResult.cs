namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>What the same stored trades would have returned with and without the scale (descriptive, before costs). Nothing here is a recommendation.</summary>
/// <param name="Trades">Number of trades.</param>
/// <param name="UnscaledTotalR">Total R as stored.</param>
/// <param name="ScaledTotalR">Total R with each trade's R times its scale.</param>
/// <param name="UnscaledMaxDrawdownR">Largest fall of the cumulative R from a peak (the start is a peak of 0), as stored.</param>
/// <param name="ScaledMaxDrawdownR">The same with the scale.</param>
/// <param name="DrawdownChangePct">Change of the maximum drawdown in percent of the unscaled one (negative is lower); null when the unscaled one is 0.</param>
/// <param name="TotalChangePct">Change of the total R in percent of the unscaled one (negative is lower); null when the unscaled one is 0.</param>
/// <param name="Halves">The first and the second half of the trades in decision order.</param>
/// <param name="Summary">The words, for example <c>max drawdown 20 % lower, total R 40 % lower</c>.</param>
public sealed record SizingStudyResult(
    int Trades,
    double UnscaledTotalR,
    double ScaledTotalR,
    double UnscaledMaxDrawdownR,
    double ScaledMaxDrawdownR,
    double? DrawdownChangePct,
    double? TotalChangePct,
    IReadOnlyList<StudyHalf> Halves,
    string Summary)
{
    /// <summary>Printed with every result.</summary>
    public const string Caveat = "descriptive, before costs, same trades";
}
