namespace XDrishti.Domain.Research;

/// <summary>The random-entry walk of one instrument: one <see cref="NullTradeDraws"/> for each trade of the plan on that instrument.</summary>
/// <param name="Trades">The draws of the instrument's trades.</param>
public sealed record NullWalkResult(IReadOnlyList<NullTradeDraws> Trades);
