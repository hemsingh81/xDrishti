namespace XDrishti.Domain.Learning;

/// <summary>A question to <see cref="PointInTimeCells"/>: the cell of a trade and the India date of its decision.</summary>
public sealed record CellAsk(string TradeKey, string CellKey, DateOnly DecisionDate);
