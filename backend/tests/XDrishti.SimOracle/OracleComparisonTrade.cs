namespace XDrishti.SimOracle;

/// <summary>
/// One closed trade as the comparison arithmetic of the oracle reads it (spec P4-05, D5 to D7): the key it is joined on, the result in the unit of the table (R filled for a gross run, R net for a costed one), the exact result, the adverse and
/// favourable excursions and the minutes it was held. Nothing else about a trade is needed, which is the point: the arithmetic is written from the text of the spec and from these seven numbers.
/// </summary>
/// <param name="Key">The key the entries of different candidates are joined on.</param>
/// <param name="R">The result in the unit of the table.</param>
/// <param name="RExact">The result from the exact prices (decides who is a winner of the diagnostics).</param>
/// <param name="Mae">The maximum adverse excursion in R.</param>
/// <param name="Mfe">The maximum favourable excursion in R.</param>
/// <param name="Reason">Why the trade left.</param>
/// <param name="HeldMinutes">Minutes from the entry minute to the exit minute.</param>
public sealed record OracleComparisonTrade(string Key, double R, double RExact, double Mae, double Mfe, OracleExitReason Reason, double HeldMinutes);
