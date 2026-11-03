namespace XDrishti.Domain.PairResearch;

/// <summary>What a walk counted besides its trades (never dropped silently): decisions with no z, entries not made because a leg had no bar, pairs not sizable, entries at or beyond the stop level, and exits that used a leg's later bar.</summary>
public sealed record PairWalkCounters(int ZUnavailable, int LegUnfilled, int NotSizable, int BeyondStop, int LegLate)
{
    public static PairWalkCounters Zero { get; } = new(0, 0, 0, 0, 0);

    public PairWalkCounters Plus(PairWalkCounters other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new PairWalkCounters(ZUnavailable + other.ZUnavailable, LegUnfilled + other.LegUnfilled, NotSizable + other.NotSizable, BeyondStop + other.BeyondStop, LegLate + other.LegLate);
    }
}
