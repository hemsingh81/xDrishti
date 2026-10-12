namespace XDrishti.Domain.Market.Baskets;

/// <summary>Largest values the basket endpoints accept.</summary>
public static class BasketLimits
{
    public const int MaxNameLength = 60;

    public const int MaxDescriptionLength = 500;

    public const int MaxNoteLength = 500;

    /// <summary>Instruments a basket holds at the same time.</summary>
    public const int MaxActiveMembers = 2_000;

    /// <summary>Instrument ids in one add, remove or transfer call (the same as for tracking).</summary>
    public const int MaxIdsPerCall = TrackingLimits.MaxIdsPerCall;

    /// <summary>Entries the history call returns at most.</summary>
    public const int MaxHistoryTake = 1_000;

    public const int DefaultHistoryTake = 200;
}
