namespace XDrishti.Domain.Readiness;

/// <summary>What one evidence set says about "evidence only from tier C" (spec CF-11 D3, owner's reading of 2026-11-07).</summary>
public enum TierSetState
{
    /// <summary>The set could not be read, or its tiers could not be told apart from tier C. Never a pass.</summary>
    Unavailable = 0,

    /// <summary>The set has no trade. No trades is not tier C.</summary>
    Absent = 1,

    /// <summary>The set has at least one trade in tier A or B, so it is not only tier C.</summary>
    Mixed = 2,

    /// <summary>The set has trades and every one of them is tier C.</summary>
    AllTierC = 3,
}
