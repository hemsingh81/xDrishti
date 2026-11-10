namespace XDrishti.Domain.Baskets.Derived;

/// <summary>
/// The values the engine reads for one instrument. <c>Benchmark</c> is the Nifty 50 daily series (needed by baskets 5 and 14).
/// <c>CorporateActionUnresolved</c> is true when an unresolved corporate-action suggestion sits in the window.
/// </summary>
public sealed record InstrumentBars(
    string Symbol,
    Bar[] Daily,
    IntradayBar[]? Intraday = null,
    DayFlag[]? Flags = null,
    Bar[]? Benchmark = null,
    bool CorporateActionUnresolved = false,
    string? Industry = null,
    double TickSize = 0.05);
