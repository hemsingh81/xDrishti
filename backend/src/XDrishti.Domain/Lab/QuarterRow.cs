namespace XDrishti.Domain.Lab;

/// <summary>One calendar quarter with at least one trade: the mean R of the trades whose entry fell in it.</summary>
/// <param name="Year">The calendar year.</param>
/// <param name="Quarter">The quarter, 1 to 4 (January to March is 1).</param>
/// <param name="Trades">The trades of the quarter, at least 1.</param>
/// <param name="MeanR">Their mean R.</param>
public sealed record QuarterRow(int Year, int Quarter, int Trades, double MeanR);
