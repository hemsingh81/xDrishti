namespace XDrishti.Domain.Market.Strength;

/// <summary>The stored rows of one instrument that a run reads (BK-09), for <c>mom_rank</c>, <c>rs_vs_index</c> and <c>sector_rs</c>: only rows as of a session before the window end, so the engine never sees a later row.</summary>
public sealed record RelativeStrengthInputs(IReadOnlyList<StrengthPoint> Points)
{
    /// <summary>The feature names the engine reads from these inputs.</summary>
    public static IReadOnlyList<string> Features { get; } = ["mom_rank", "rs_vs_index", "sector_rs"];
}
