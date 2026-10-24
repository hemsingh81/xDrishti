using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>One trade of a report as a screen lists it: what it was, when, and the R of the report's basis (gross or net). Style and exit are the labels of its run; null is not recorded.</summary>
/// <param name="RunKey">The run it came from.</param>
/// <param name="TradeKey">The stored signal key.</param>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Timeframe">The timeframe code.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="BasketIds">The baskets of the run's universe the instrument was in; empty when not recorded.</param>
/// <param name="EntryDate">India date of the entry.</param>
/// <param name="ExitDate">India date of the exit.</param>
/// <param name="R">R on the basis of the report.</param>
/// <param name="SessionsHeld">Sessions held, both ends included.</param>
/// <param name="Style">The run's style label; null when not recorded.</param>
/// <param name="Exit">The run's exit label; null when not recorded.</param>
/// <param name="Provisional">The run is marked provisional.</param>
public sealed record ReportTradeLine(
    string RunKey,
    string TradeKey,
    string Strategy,
    string Timeframe,
    string Side,
    string Symbol,
    ValueList<string> BasketIds,
    DateOnly EntryDate,
    DateOnly ExitDate,
    double R,
    int SessionsHeld,
    string? Style,
    string? Exit,
    bool Provisional);
