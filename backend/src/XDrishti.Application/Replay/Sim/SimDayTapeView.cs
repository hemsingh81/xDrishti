namespace XDrishti.Application.Replay.Sim;

/// <summary>One stored 1-minute bar of an instrument in play (SR-04 tape). <paramref name="At"/> is the start of the minute.</summary>
public sealed record SimTapeBar(string Symbol, DateTimeOffset At, double Open, double High, double Low, double Close, long Volume);

/// <summary>A trade event at its own minute (SR-04 tape): <c>entry</c> (price = the fill, stop = the initial stop) or <c>exit</c> (price = the exit, with the gross P&amp;L and the reason). The stop is not moved by the replay, so there is no stop-move event.</summary>
public sealed record SimTapeEvent(DateTimeOffset At, string Kind, long TradeNo, string Symbol, string Side, int Qty, decimal? Price, decimal Stop, decimal? GrossPnl, string? Reason);

/// <summary>An open position on the live board at the clock minute: the last price is the close of the instrument's last bar of the day up to the minute (null before its first bar; the running P&amp;L is then 0).</summary>
public sealed record SimBoardPosition(long TradeNo, string Symbol, string Side, int Qty, DateTimeOffset EntryAt, decimal EntryPrice, decimal Stop, decimal? LastPrice, decimal RunningPnl);

/// <summary>
/// The live board at the clock minute <paramref name="At"/> (SR-04, AC 6), from the bars and the events up to that minute only. <paramref name="Equity"/> is realised, as <see cref="Domain.Paper.PaperLedger"/> counts it: the equity at the previous close plus the gross P&amp;L of the exits of the day up to the minute; at the end
/// of the day it equals the settled closing equity (AC 5). <paramref name="Unrealised"/> is the sum of the running P&amp;L of the open positions.
/// </summary>
public sealed record SimBoard(DateTimeOffset At, IReadOnlyList<SimBoardPosition> Open, decimal Realised, decimal Unrealised, decimal EquityOpen, decimal Equity);

/// <summary>
/// One page of a settled day's tape (SR-04 part B, ADR 0293): the stored 1-minute bars of the instruments in play (picked, held or traded that day) and the trade events whose minute lies in [<paramref name="From"/>, <paramref name="To"/>], and the live board at <paramref name="To"/>. Nothing after <paramref name="To"/> is in it.
/// Replaying the pages in order, with any paging, gives the settled day's trades and equity.
/// </summary>
public sealed record SimDayTapeView(Guid RunId, DateOnly Session, DateTimeOffset From, DateTimeOffset To, DateTimeOffset MarketOpen, DateTimeOffset MarketClose, IReadOnlyList<string> Symbols, IReadOnlyList<SimTapeBar> Bars, IReadOnlyList<SimTapeEvent> Events, SimBoard Board, string CostsLabel);
