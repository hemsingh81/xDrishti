namespace XDrishti.Domain.Lab;

/// <summary>What one kind of look (strategy, model, system) can still do at the block in force, as read by the seal step (HO-01 D4).</summary>
public enum LookAvailability
{
    /// <summary>The kind's fresh slot at the block is used.</summary>
    Used,

    /// <summary>The slot is free and no eligible subject exists (or the kind's look path is not built yet).</summary>
    NoSubject,

    /// <summary>The slot is free and an eligible subject exists: a look is still available.</summary>
    Available,

    /// <summary>The read failed. Never counts as a pass: the block is kept.</summary>
    Unavailable,
}
