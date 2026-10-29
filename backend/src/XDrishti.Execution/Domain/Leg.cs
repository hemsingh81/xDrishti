namespace XDrishti.Execution.Domain;

/// <summary>One order leg as the broker reports it. Statuses applied out of order never move a leg back; a late lower status is ignored and counted.</summary>
public sealed class Leg(LegKind kind, int qty)
{
    public LegKind Kind { get; } = kind;

    public int Qty { get; } = qty;

    public LegStatus Status { get; private set; } = LegStatus.New;

    public int FilledQty { get; private set; }

    public int StaleUpdates { get; private set; }

    public static bool IsFinal(LegStatus status) => status >= LegStatus.Cancelled;

    public static int Rank(LegStatus status) => status switch
    {
        LegStatus.New => 0,
        LegStatus.Sent => 1,
        LegStatus.Transit => 2,
        LegStatus.Pending => 3,
        LegStatus.PartTraded => 4,
        LegStatus.Traded => 5,
        _ => 6,
    };

    /// <summary>Returns whether the update moved the leg. A lower rank, or any change after a final status, is counted in <see cref="StaleUpdates"/> and ignored. The filled quantity never decreases and never exceeds <see cref="Qty"/>.</summary>
    public bool Apply(LegStatus status, int filledQty)
    {
        if (IsFinal(Status) || Rank(status) < Rank(Status))
        {
            StaleUpdates++;
            return false;
        }

        Status = status;
        FilledQty = Math.Max(FilledQty, Math.Clamp(filledQty, 0, Qty));
        return true;
    }
}
