namespace XDrishti.Domain.Lab;

/// <summary>Where a block of the hold-out chain stands (HO-01, D2). Derived from the block, the calendar and the stored looks and releases; never stored.</summary>
public enum HoldOutBlockState
{
    /// <summary>The last session of the block has not closed yet: no look may read it.</summary>
    Filling,

    /// <summary>Every session of the block has closed and no fresh look was taken.</summary>
    Sealed,

    /// <summary>At least one fresh look was taken at the block.</summary>
    Looked,

    /// <summary>The owner (or the auto option) released the block: it is development now, labelled <c>seen_holdout</c>.</summary>
    Released,
}
