using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Strength;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Strength;

/// <summary>The relative-strength tables (BK-09). A day is written in one transaction and only when it has no row: the second writer, or a rerun, inserts nothing. The runtime role has insert and select only (the migration revokes the rest).</summary>
internal sealed class RelativeStrengthStore(XDrishtiDbContext db) : IRelativeStrengthStore
{
    public async ValueTask<IReadOnlySet<DateOnly>> DatesWithRowsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var dates = await db.RelativeStrengths.AsNoTracking().Where(r => r.AsOf >= from && r.AsOf <= to).Select(r => r.AsOf).Distinct().ToListAsync(cancellationToken);
        var sectorDates = await db.SectorStrengths.AsNoTracking().Where(r => r.AsOf >= from && r.AsOf <= to).Select(r => r.AsOf).Distinct().ToListAsync(cancellationToken);
        return dates.Concat(sectorDates).ToHashSet();
    }

    public async ValueTask<bool> InsertDayAsync(DateOnly asOf, IReadOnlyList<StrengthRow> rows, IReadOnlyList<SectorRow> sectors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(sectors);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (await db.RelativeStrengths.AnyAsync(r => r.AsOf == asOf, ct) || await db.SectorStrengths.AnyAsync(r => r.AsOf == asOf, ct))
            {
                return false;
            }

            db.RelativeStrengths.AddRange(rows.Select(r => new RelativeStrengthRecord
            {
                AsOf = r.AsOf,
                InstrumentId = r.InstrumentId,
                BasketId = r.BasketId,
                Lookback = r.Lookback,
                Skip = r.Skip,
                Score = r.Score,
                PctRank = r.PctRank,
                NRanked = r.NRanked,
                State = r.State,
                MembershipBasis = r.MembershipBasis,
            }));
            db.SectorStrengths.AddRange(sectors.Select(s => new SectorStrengthRecord
            {
                AsOf = s.AsOf,
                Sector = s.Sector,
                WindowSessions = s.WindowSessions,
                Ret = s.Ret,
                Rank = s.Rank,
                NMembers = s.NMembers,
                State = s.State,
            }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return true;
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StrengthRow>> ListAsync(Guid? basketId, DateOnly? asOf, int take, CancellationToken cancellationToken)
    {
        var query = db.RelativeStrengths.AsNoTracking().Where(r => r.BasketId != Guid.Empty);
        if (basketId is { } id)
        {
            query = query.Where(r => r.BasketId == id);
        }

        if (asOf is { } day)
        {
            query = query.Where(r => r.AsOf == day);
        }

        var rows = await query.OrderByDescending(r => r.AsOf).ThenBy(r => r.BasketId).ThenBy(r => r.Lookback).ThenBy(r => r.PctRank == null).ThenByDescending(r => r.PctRank).ThenBy(r => r.InstrumentId)
            .Take(take).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new StrengthRow(r.AsOf, r.InstrumentId, r.BasketId, r.Lookback, r.Skip, r.Score, r.PctRank, r.NRanked, r.State, r.MembershipBasis))];
    }
}
