namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The reader of a claimed hold-out block (SR-03 D4): lets the replay's bar reader go up to the end of the block, and no further. Built only by <see cref="HoldOutSystemRunner"/>, only from a <see cref="HoldOutClaim"/>; every other replay path keeps the training clamp.
/// </summary>
internal sealed class HoldOutBlockReader
{
    internal HoldOutBlockReader(HoldOutClaim claim, DateOnly end)
    {
        ArgumentNullException.ThrowIfNull(claim);
        Claim = claim;
        End = end;
    }

    /// <summary>The claim this reader stands on.</summary>
    public HoldOutClaim Claim { get; }

    /// <summary>The last session that may be read: the block's end.</summary>
    public DateOnly End { get; }
}
