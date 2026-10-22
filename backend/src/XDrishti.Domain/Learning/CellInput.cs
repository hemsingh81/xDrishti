namespace XDrishti.Domain.Learning;

/// <summary>The trades of one cell, in any order.</summary>
/// <param name="Cell">Who the cell is.</param>
/// <param name="Costed">R is net of charges; false means gross (from the filled prices) and every row says so.</param>
/// <param name="Trades">The closed trades.</param>
public sealed record CellInput(CellId Cell, bool Costed, IReadOnlyList<CellTrade> Trades);
