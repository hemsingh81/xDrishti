namespace XDrishti.Domain.Replay;

/// <summary>What the replay of a taken item gave on its session: a filled entry, or why there is none.</summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="CellKey">The cell.</param>
/// <param name="Session">The session of the fill (the session after the plan's).</param>
/// <param name="Status"><c>filled</c>, <c>not_triggered</c> or <c>pending</c>.</param>
/// <param name="Qty">The quantity; 0 when not filled.</param>
/// <param name="EntryPrice">The filled entry; null when not filled.</param>
/// <param name="Detail">The reason when not filled.</param>
public sealed record ReplayFill(string Symbol, string Side, string CellKey, DateOnly Session, string Status, int Qty, decimal? EntryPrice, string Detail)
{
    /// <summary>The status of an entry that was filled.</summary>
    public const string Filled = "filled";

    /// <summary>The status of an item whose trigger never came.</summary>
    public const string NotTriggered = "not_triggered";

    /// <summary>The status of an item the replay could not decide (refusal, no instrument).</summary>
    public const string Pending = "pending";
}
