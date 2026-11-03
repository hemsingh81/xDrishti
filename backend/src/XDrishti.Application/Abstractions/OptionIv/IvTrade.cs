namespace XDrishti.Application.Abstractions.OptionIv;

/// <summary>One trade of a stored run as the <c>iv-1</c> extraction sees it: the instrument, the trade's key and its decision instant. Nothing of its outcome is given.</summary>
public readonly record struct IvTrade(Guid InstrumentId, string TradeKey, DateTimeOffset At);
