namespace XDrishti.Domain.Learning;

/// <summary>
/// What the decay monitor says about a forward series (spec CF-12, D1; ADR 0218). A reading, not an action: nothing here pauses, proposes or changes anything.
/// </summary>
/// <param name="Trades">Trades read.</param>
/// <param name="PendingTrades">Trades after the last completed window (not yet judged).</param>
/// <param name="Windows">The completed windows, oldest first.</param>
/// <param name="Streak">Completed windows below their threshold in a row, counted back from the latest.</param>
/// <param name="TwiceInARow">True when the latest two completed windows are both below (the streak is 2 or more); a window above resets it.</param>
/// <param name="FirstTwiceAtWindow">The 1-based window at which the streak first reached 2, or null when it never did.</param>
/// <param name="CusumEvents">The alarms and clears of the P6-09 CUSUM on the same series, against the same reference (the existing detector, unchanged).</param>
/// <param name="CusumAlarm">True when the last CUSUM event is an alarm.</param>
/// <param name="Quarters">The by-quarter table, oldest first.</param>
/// <param name="WeakQuarterShare">The share of quarters with a mean below the floor; null with no quarter.</param>
public sealed record DecayReport(
    int Trades,
    int PendingTrades,
    IReadOnlyList<DecayWindow> Windows,
    int Streak,
    bool TwiceInARow,
    int? FirstTwiceAtWindow,
    IReadOnlyList<DriftEvent> CusumEvents,
    bool CusumAlarm,
    IReadOnlyList<DecayQuarter> Quarters,
    double? WeakQuarterShare);
