namespace XDrishti.Application.Abstractions.Market.Strength;

/// <summary>One stored momentum or index-relative row of <c>market.relative_strength_daily</c> (BK-09). <see cref="BasketId"/> is <see cref="Guid.Empty"/> for the index-relative row (lookback 20, skip 0, the score is the percentage points).</summary>
public sealed record StrengthRow(DateOnly AsOf, Guid InstrumentId, Guid BasketId, int Lookback, int Skip, double? Score, double? PctRank, int NRanked, string State, string MembershipBasis);

/// <summary>One stored row of <c>market.sector_strength_daily</c> (BK-09): the equal-weight member return over the window and the rank among sectors.</summary>
public sealed record SectorRow(DateOnly AsOf, string Sector, int WindowSessions, double? Ret, int? Rank, int NMembers, string State);

/// <summary>The relative-strength tables (BK-09). Insert-once: a day that has rows is never rewritten, and the runtime role has insert and select only.</summary>
public interface IRelativeStrengthStore
{
    /// <summary>The dates in the range that have any row.</summary>
    ValueTask<IReadOnlySet<DateOnly>> DatesWithRowsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Writes the rows of one day in one transaction; false and nothing written when the day already has rows.</summary>
    ValueTask<bool> InsertDayAsync(DateOnly asOf, IReadOnlyList<StrengthRow> rows, IReadOnlyList<SectorRow> sectors, CancellationToken cancellationToken);

    /// <summary>The newest <paramref name="take"/> momentum rows, newest day first, optionally of one basket and one day.</summary>
    ValueTask<IReadOnlyList<StrengthRow>> ListAsync(Guid? basketId, DateOnly? asOf, int take, CancellationToken cancellationToken);
}
