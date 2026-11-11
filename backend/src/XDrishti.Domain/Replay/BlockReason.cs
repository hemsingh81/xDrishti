namespace XDrishti.Domain.Replay;

/// <summary>The closed list of reasons a candidate was not taken in a replayed session (SR-01). A blocked candidate has exactly one.</summary>
public enum BlockReason
{
    /// <summary>The market gate (no-trade or reduced cut) or the selection eligibility gate refused it.</summary>
    Gate,

    /// <summary>A smart rule set it aside (same bet as a better ticket or an open trade, a rule that removed half), or another portfolio check (sector, basket, correlation, one per symbol) refused it.</summary>
    SmartRule,

    /// <summary>Too many open positions, entries per day or plan items.</summary>
    MaxPositions,

    /// <summary>The trade's risk, the total open risk or the net exposure limit.</summary>
    TotalRisk,

    /// <summary>The correlation cluster's open risk cap.</summary>
    ClusterCap,

    /// <summary>The capital did not fit it (sizing down to a basket budget could not keep it).</summary>
    Capital,

    /// <summary>The session's bars are incomplete for too much of the universe: no new entries.</summary>
    DataMissing,

    /// <summary>The cell's strategy has no version under the version rule.</summary>
    NoVersion,
}

/// <summary>The stored text of a <see cref="BlockReason"/>.</summary>
public static class BlockReasons
{
    /// <summary>Every reason, in declaration order.</summary>
    public static IReadOnlyList<BlockReason> All { get; } = [.. Enum.GetValues<BlockReason>()];

    /// <summary>The code of a reason: <c>gate</c>, <c>smart_rule</c>, <c>max_positions</c>, <c>total_risk</c>, <c>cluster_cap</c>, <c>capital</c>, <c>data_missing</c>, <c>no_version</c>.</summary>
    public static string Code(this BlockReason reason) => reason switch
    {
        BlockReason.Gate => "gate",
        BlockReason.SmartRule => "smart_rule",
        BlockReason.MaxPositions => "max_positions",
        BlockReason.TotalRisk => "total_risk",
        BlockReason.ClusterCap => "cluster_cap",
        BlockReason.Capital => "capital",
        BlockReason.DataMissing => "data_missing",
        BlockReason.NoVersion => "no_version",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
