namespace XDrishti.Application.Lab;

/// <summary>
/// The probability of backtest overfitting of the sweep (CF-03 part B, ADR 0228), shown beside the verdict. Every figure is null when it could not be computed and <see cref="Note"/> then starts with <c>unavailable: </c>; an unavailable figure never moves an outcome.
/// <c>Capped</c> is true only when the owner's Gate mode capped a PASS at MARGINAL because of it.
/// </summary>
/// <param name="Mode">The mode when it was computed.</param>
/// <param name="Variants">The variants compared: the base run and the sweep points that ran at least one trade.</param>
/// <param name="Blocks">S, the blocks the window was cut into.</param>
/// <param name="Splits">The splits tried, <c>C(S, S/2)</c>; 0 when unavailable.</param>
/// <param name="Pbo">The share of splits in which the in-sample best variant is at or below the out-of-sample median.</param>
/// <param name="MedianLogit">The median logit of the out-of-sample rank over the splits (below 0: the winner tends to fall below the median).</param>
/// <param name="MaxPbo">The limit the Gate compares with; it decides nothing outside Gate mode.</param>
/// <param name="Metric"><c>Mean</c> or <c>Sharpe</c>: what picks and ranks the best variant.</param>
/// <param name="Capped">True when a PASS was capped at MARGINAL by this figure (Gate mode only).</param>
/// <param name="Settings">The settings that produced the figure.</param>
/// <param name="Note">The reading and the honest limits, or the unavailable reason.</param>
public sealed record PboView(
    string Mode,
    int Variants,
    int Blocks,
    int Splits,
    double? Pbo,
    double? MedianLogit,
    double MaxPbo,
    string Metric,
    bool Capped,
    string Settings,
    string Note);

/// <summary>One variant's daily series (net R by exit date, rounded to 4 places) and who it is: <c>base</c> or the swept parameter and value.</summary>
public sealed record VariantSeriesRowView(string Label, IReadOnlyList<double> Values);

/// <summary>The daily series PBO was computed from (CF-03 part B): one value per variant per date, so the figure can be recomputed from the stored document. Null when the PBO mode is Off or a daily value was not finite.</summary>
public sealed record VariantSeriesView(IReadOnlyList<DateOnly> Dates, IReadOnlyList<VariantSeriesRowView> Variants);
