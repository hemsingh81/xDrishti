namespace XDrishti.Application.Replay.Sim;

/// <summary>A trade of the simulation book that enters or exits on the day (SR-04): the exit only when it is on or before the day, so a day never shows a later exit. Gross (ADR 0292). The instrument id, exit price and exit reason were added for the tape (part B); a day stored without them reads them as null.</summary>
public sealed record SimDayTrade(long TradeNo, string Symbol, string Side, string CellKey, int Qty, DateOnly EntrySession, DateTimeOffset EntryAt, decimal EntryPrice, decimal StopPrice, bool Swing, DateOnly? ExitSession, DateTimeOffset? ExitAt, decimal? GrossPnl, double? R, Guid? InstrumentId = null, decimal? ExitPrice = null, string? ExitReason = null);
