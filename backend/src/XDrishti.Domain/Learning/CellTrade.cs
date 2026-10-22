namespace XDrishti.Domain.Learning;

/// <summary>One closed trade as the cell statistics read it: the India date of its exit (when it became known), R in the unit of its cell, and the excursions in R.</summary>
/// <param name="ExitDate">India date of the exit; the trade counts at an as-of date on or after it, never before.</param>
/// <param name="R">The result in R (net of charges when the cell is costed, else from the filled prices).</param>
/// <param name="MaeR">Maximum adverse excursion in R, not below 0.</param>
/// <param name="MfeR">Maximum favourable excursion in R, not below 0.</param>
public sealed record CellTrade(DateOnly ExitDate, double R, double MaeR, double MfeR);
