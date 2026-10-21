namespace XDrishti.Domain.Research;

/// <summary>
/// One fill of one order of one position (P4-01, D7). A trade that entered and left in one fill each has no legs; a trade that left in several carries them, the entry first and the last exit last, in ascending order of instant.
/// </summary>
/// <param name="EpochSeconds">Start of the minute the leg filled in.</param>
/// <param name="Price">The exact price of the fill.</param>
/// <param name="FilledPrice">The price after slippage and the price grid.</param>
/// <param name="Share">The fraction of the maximum quantity of the trade this leg holds; the exit legs sum to the entry and add legs.</param>
/// <param name="Kind">What the leg did.</param>
/// <param name="Reason">For an exit leg, why it left: an <see cref="ExitReason"/> name for the last one, the code of the rule for a partial one; empty for an entry or an add.</param>
public sealed record Leg(long EpochSeconds, double Price, double FilledPrice, decimal Share, LegKind Kind, string Reason);
