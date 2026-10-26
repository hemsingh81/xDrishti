namespace XDrishti.Domain.Paper;

/// <summary>The stored facts a review of a window rests on (spec P10-03).</summary>
/// <param name="Tickets">Tickets of the window's sessions (any group).</param>
/// <param name="Trades">Every stored paper trade of the epoch (the pace counts the whole epoch).</param>
/// <param name="Skips">Every stored skip of the epoch.</param>
/// <param name="Fills">Discretionary real fills of the window; null when they could not be read.</param>
/// <param name="FillsUnavailable">Why, when <paramref name="Fills"/> is null.</param>
public sealed record PaperRecordFacts(IReadOnlyList<ReviewTicket> Tickets, IReadOnlyList<ReviewTrade> Trades, IReadOnlyList<ReviewSkip> Skips, IReadOnlyList<ReviewFill>? Fills, string? FillsUnavailable);
