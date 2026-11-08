namespace XDrishti.Domain.Readiness;

/// <summary>One evidence set (the Lab out-of-sample trades, or the forward shadow and paper trades) read for the hard fail "evidence only from tier C". Build it with <see cref="Of"/> or <see cref="Unreadable"/>.</summary>
/// <param name="Name">The set, as it is named beside the hard fail.</param>
/// <param name="State">What the set says.</param>
/// <param name="Counts">The trades by tier; null when the set could not be read.</param>
/// <param name="Reason">Why the set is unavailable, or a short note; empty when there is nothing to add.</param>
public sealed record TierSet(string Name, TierSetState State, TierCounts? Counts, string Reason)
{
    /// <summary>A set that could not be read: unavailable, with the reason.</summary>
    public static TierSet Unreadable(string name, string reason) => new(name, TierSetState.Unavailable, null, reason);

    /// <summary>
    /// A set from its counts. No trade is absent (not tier C); every trade in tier C is all tier C; at least one trade in tier A or B is mixed; trades that are tier C or without a tier, with none in A or B, cannot be told apart from all-tier-C, so the set is unavailable
    /// (an unavailable tier is never counted as tier C and never as a pass). Negative counts are not counts: unavailable.
    /// </summary>
    public static TierSet Of(string name, TierCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.A < 0 || counts.B < 0 || counts.C < 0 || counts.Unavailable < 0)
        {
            return new TierSet(name, TierSetState.Unavailable, null, "the tier counts are not valid numbers");
        }

        if (counts.Total == 0)
        {
            return new TierSet(name, TierSetState.Absent, counts, "no trades");
        }

        if (counts.C == counts.Total)
        {
            return new TierSet(name, TierSetState.AllTierC, counts, string.Empty);
        }

        if (counts.A + counts.B > 0)
        {
            return new TierSet(name, TierSetState.Mixed, counts, string.Empty);
        }

        return new TierSet(name, TierSetState.Unavailable, counts, $"{counts.Unavailable} of {counts.Total} trades have no tier and the rest are tier C, so it cannot be told whether all of them are tier C");
    }

    /// <summary>The set in one line, for the evidence shown beside the hard fail.</summary>
    public string Line => State switch
    {
        TierSetState.Unavailable => $"{Name}: unavailable ({Reason})" + (Counts is null ? string.Empty : $"; {Counts.Text}"),
        TierSetState.Absent => $"{Name}: no trades (not tier C)",
        TierSetState.Mixed => $"{Name}: {Counts!.Text} (not only tier C)",
        _ => $"{Name}: {Counts!.Text} (only tier C)",
    };
}
