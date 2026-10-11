namespace XDrishti.Domain.Portfolio;

/// <summary>What a sync did, per holding: found new (or back), quantity up, quantity down, gone, or nothing but the price.</summary>
public sealed record SyncCounts(int Added, int Increased, int Decreased, int Closed, int Unchanged)
{
    public static SyncCounts None { get; } = new(0, 0, 0, 0, 0);

    /// <summary>True when anything but prices changed.</summary>
    public bool HasChanges => Added + Increased + Decreased + Closed > 0;

    public int Total => Added + Increased + Decreased + Closed + Unchanged;

    public SyncCounts Count(HoldingChange change) => change switch
    {
        HoldingChange.Added => this with { Added = Added + 1 },
        HoldingChange.Increased => this with { Increased = Increased + 1 },
        HoldingChange.Decreased => this with { Decreased = Decreased + 1 },
        HoldingChange.Closed => this with { Closed = Closed + 1 },
        _ => this with { Unchanged = Unchanged + 1 },
    };
}
