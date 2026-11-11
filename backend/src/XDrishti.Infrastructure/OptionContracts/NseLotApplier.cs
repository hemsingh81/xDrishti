using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Domain.Options;
using XDrishti.Infrastructure.Market.SystemBaskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.OptionContracts;

/// <summary>
/// Writes the NSE lots of a fetch (BK-05b) into <c>options.lot_sizes</c> as <c>nse_mktlots</c> rows (unconfirmed, with the file URL), one set-based insert that skips an underlying whose newest <c>nse_mktlots</c> row already says the same lot, and the
/// fetch row with the source state, all in one transaction: any failure rolls everything back (the tracked rows are dropped) and the result is null. The table is insert-only for the runtime role; this class never updates or deletes a row.
/// </summary>
internal sealed class NseLotApplier(XDrishtiDbContext db, BasketSourceStore store) : INseLotApplier
{
    private const string InsertChanged =
        $"""
        INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed)
        SELECT x.underlying_id, @today, x.lot_size, '{LotSizeEntry.NseMktLots}', @url, false
        FROM unnest(@ids, @lots) AS x(underlying_id, lot_size)
        WHERE x.lot_size IS DISTINCT FROM (
            SELECT l.lot_size FROM options.lot_sizes l
            WHERE l.underlying_id = x.underlying_id AND l.source = '{LotSizeEntry.NseMktLots}'
            ORDER BY l.effective_from DESC, l.id DESC
            LIMIT 1)
        """;

    public async ValueTask<int?> ApplyAsync(NseLotApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var lots = application.Lots.GroupBy(l => l.UnderlyingId).Select(g => g.First()).ToList();
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var written = await db.Database.ExecuteSqlRawAsync(
                    InsertChanged,
                    [
                        new NpgsqlParameter("today", application.Today),
                        new NpgsqlParameter("url", application.SourceUrl),
                        new NpgsqlParameter("ids", lots.Select(l => l.UnderlyingId).ToArray()),
                        new NpgsqlParameter("lots", lots.Select(l => l.LotSize).ToArray()),
                    ],
                    cancellationToken);
                store.Stage(application.Source, application.Fetch(written));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (int?)written;
            });
        }
        catch (Exception ex) when (ex is DbUpdateException or NpgsqlException or InvalidOperationException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }
}
