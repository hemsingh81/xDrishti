using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// One closed simulated trade as a report reads it. <see cref="TradeKey"/> is the stored signal key of the trade (<c>strategy|symbol|side|decision date</c>), unique per signal: the same signal found in two runs is the same trade, which the population rule
/// refuses to count twice (or, when the runs differ in style or exit, treats as one signal under two labels that a report must split).
/// </summary>
/// <param name="RunKey">The run it came from.</param>
/// <param name="TradeKey">The stored signal key.</param>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Timeframe">The evaluation timeframe code.</param>
/// <param name="Side">The side, <c>BUY</c> or <c>SELL</c>.</param>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Symbol">Its symbol.</param>
/// <param name="BasketIds">The baskets the instrument belonged to in the run (as of the run); empty when not recorded.</param>
/// <param name="EntryDate">India date of the entry.</param>
/// <param name="ExitDate">India date of the exit.</param>
/// <param name="RGross">R from the filled prices.</param>
/// <param name="RNet">R net of charges; null for a gross trade.</param>
/// <param name="SessionsHeld">Sessions from the entry to the exit, both included.</param>
/// <param name="MaeR">Maximum adverse excursion in R (never below 0); null when not recorded.</param>
/// <param name="MfeR">Maximum favourable excursion in R; null when not recorded.</param>
/// <param name="EntryMinuteIst">Minutes after midnight, India time, of the entry fill; null when not recorded.</param>
/// <param name="ExitReason">Why the trade closed (the stored reason); null when not recorded.</param>
/// <param name="Sector">The sector of the instrument as of the read (not as of the trade); null when the master has none.</param>
public sealed record ReportTrade(
    string RunKey,
    string TradeKey,
    string Strategy,
    string Timeframe,
    string Side,
    Guid InstrumentId,
    string Symbol,
    ValueList<string> BasketIds,
    DateOnly EntryDate,
    DateOnly ExitDate,
    double RGross,
    double? RNet,
    int SessionsHeld,
    double? MaeR = null,
    double? MfeR = null,
    int? EntryMinuteIst = null,
    string? ExitReason = null,
    string? Sector = null);
