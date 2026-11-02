namespace XDrishti.Domain.Learning.Extensions;

/// <summary>What the event-risk filter would do to one trade of an unfiltered run (P15-10, protocol F).</summary>
public enum FilterFate
{
    /// <summary>The results event lies within the window: the filter removes it.</summary>
    Skipped,

    /// <summary>The next results event is outside the window: the filter keeps it.</summary>
    Taken,

    /// <summary>The calendar cannot say (no entry, only cancelled, no upcoming entry): the filter fails closed and removes it too (ADR 0182).</summary>
    Unavailable,
}
