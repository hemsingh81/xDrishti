namespace XDrishti.Domain.Learning.Extensions;

/// <summary>What a bootstrap block is (P13-05): a run of consecutive sessions (P13) or a calendar quarter, a season (P15).</summary>
public enum BlockKind
{
    /// <summary>Blocks of <see cref="ExtensionComparisonOptions.BlockSessions"/> consecutive sessions.</summary>
    Sessions,

    /// <summary>One calendar quarter per block.</summary>
    Season,
}
