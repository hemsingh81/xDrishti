namespace XDrishti.Application.Reporting;

/// <summary>One closed trade of a report: what it was, when, and its R on the basis of the report (gross or net).</summary>
/// <param name="RunKey">The run it came from.</param>
/// <param name="TradeKey">The stored signal key.</param>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Timeframe">The timeframe code.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="BasketIds">The baskets the instrument was in when the run was made; empty when not recorded.</param>
/// <param name="EntryDate">India date of the entry.</param>
/// <param name="ExitDate">India date of the exit.</param>
/// <param name="R">R on the basis of the report.</param>
/// <param name="SessionsHeld">Sessions held, both ends included.</param>
/// <param name="Style">The run's style label; null when not recorded.</param>
/// <param name="Exit">The run's exit label; null when not recorded.</param>
/// <param name="Provisional">The run is marked provisional.</param>
/// <param name="Ordinal">The trade's position in its run: with the run key, the address of its replay (a closed trade, so open-at-end is false).</param>
public sealed record TradeLineView(
    string RunKey,
    string TradeKey,
    string Strategy,
    string Timeframe,
    string Side,
    string Symbol,
    IReadOnlyList<string> BasketIds,
    DateOnly EntryDate,
    DateOnly ExitDate,
    double R,
    int SessionsHeld,
    string? Style,
    string? Exit,
    bool Provisional,
    int Ordinal = 0);
