namespace XDrishti.Domain.Market.Context;

/// <summary>What <see cref="ContextDerivation.Derive"/> found: the records to write (oldest first), the dates still waiting for their VIX leg, and the days that were skipped with the reason.</summary>
public sealed record ContextDerivationResult(
    IReadOnlyList<ContextDayRecord> Records,
    IReadOnlyList<DateOnly> Pending,
    IReadOnlyList<SkippedContextDay> Skipped);
