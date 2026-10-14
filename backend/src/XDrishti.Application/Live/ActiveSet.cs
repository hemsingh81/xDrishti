using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Live;

/// <summary>Why an instrument is in the active set. An instrument can have several reasons (a held share with an alert); the reasons of the providers are merged.</summary>
[Flags]
public enum ActiveSetReason
{
    None = 0,

    /// <summary>An open holding of the owner.</summary>
    Holding = 1,

    /// <summary>An active price alert.</summary>
    Alert = 2,

    /// <summary>The benchmark or a context index (NIFTY 50, NIFTY BANK, INDIA VIX).</summary>
    Index = 4,
}

/// <summary>What a provider proposes for the active set: the instrument, how to subscribe to it, why, and a value (for holdings: the value held) that orders the candidates of one reason when the set must be cut.</summary>
public sealed record ActiveSetCandidate(Guid InstrumentId, string Segment, string SecurityId, FeedMode Mode, ActiveSetReason Reason, decimal Value = 0m);

/// <summary>A member of the active set: one subscription of the feed. Identified by segment and security id (<see cref="Key"/>); <see cref="Reasons"/> are merged from the providers.</summary>
public sealed record ActiveSetMember(Guid InstrumentId, string Segment, string SecurityId, FeedMode Mode, ActiveSetReason Reasons)
{
    /// <summary>The security on the feed; the security id of a member is always a positive number.</summary>
    public FeedSecurity Key => new(Segment, int.Parse(SecurityId, System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>The active set: the members in the order they were kept (alerts, indices, holdings by value) and how many candidates the cap cut off (<see cref="Cut"/>).</summary>
public sealed record ActiveSet(IReadOnlyList<ActiveSetMember> Members, int Cut = 0)
{
    public static readonly ActiveSet Empty = new([]);
}

/// <summary>What changes on the feed when the set changes: the subscriptions to add and to remove. A member whose mode changed is in both (the old mode is removed, the new one added).</summary>
public sealed record ActiveSetDiff(IReadOnlyList<ActiveSetMember> Subscribe, IReadOnlyList<ActiveSetMember> Unsubscribe)
{
    public bool IsEmpty => Subscribe.Count == 0 && Unsubscribe.Count == 0;
}
