namespace XDrishti.Domain.Research;

/// <summary>What the replay of one instrument gives: its record, its trades in the window (the trades that ended at the stop, the target or the square-off) and the <c>EndOfData</c> trades dated in the window, each by entry instant, then Buy before Sell.</summary>
public sealed record InstrumentReplay(InstrumentRun Record, IReadOnlyList<SimTrade> Trades, IReadOnlyList<SimTrade> OpenAtEnd);
