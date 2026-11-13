using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The pre-dating horizon of a fresh look (HO-01 D5, ADR 0283). A look is fresh only when its subject was frozen before the block's horizon; otherwise it is stored <c>reused_window</c> with the reason <see cref="NotPredated"/> and cannot pass. For a block of
/// k at least 2 the horizon is the block's start (India date); for block 1 it is the first stored look of any kind (with none yet, any subject of the strategy or model kind qualifies), and a <c>system</c> look is never fresh at block 1. A subject with no known freeze never qualifies
/// after block 1's first look. Pure.
/// </summary>
public static class HoldOutHorizon
{
    /// <summary>The stored reason of a look whose subject was not frozen before the horizon.</summary>
    public const string NotPredated = "not_predated";

    /// <summary>The kind of a look of the whole system (SR-03).</summary>
    public const string SystemKind = "system";

    /// <summary>Whether a subject of <paramref name="kind"/>, frozen at <paramref name="subjectFrozenAt"/>, may take the block's fresh look.</summary>
    /// <param name="kind"><c>strategy</c>, <c>model</c> or <c>system</c>.</param>
    /// <param name="firstBlock">Whether the block has no predecessor (block 1).</param>
    /// <param name="blockStart">The first date of the block.</param>
    /// <param name="subjectFrozenAt">The first plan freeze, the model registration or the SR-03 registration; null when unknown.</param>
    /// <param name="firstLookAt">When the first look of any kind at the block was stored; null when none yet (used for block 1 only).</param>
    public static bool IsPredated(string kind, bool firstBlock, DateOnly blockStart, DateTimeOffset? subjectFrozenAt, DateTimeOffset? firstLookAt)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (firstBlock)
        {
            if (string.Equals(kind, SystemKind, StringComparison.Ordinal))
            {
                return false;
            }

            return firstLookAt is null || (subjectFrozenAt is { } frozen && frozen < firstLookAt);
        }

        return subjectFrozenAt is { } at && SessionClock.DateOf(at.ToUnixTimeSeconds()) < blockStart;
    }
}
