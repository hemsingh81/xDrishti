using Npgsql;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Infrastructure.SimAll;

/// <summary>The instruments the job covers: tracked now, not retired, a share, ETF or index, with stored bars (the same set the history phase fetches).</summary>
internal sealed class SimAllUniverse(NpgsqlDataSource dataSource) : ISimAllUniverse
{
    public async ValueTask<IReadOnlyList<Guid>> InstrumentIdsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT i.id FROM market.tracked_instruments t
            JOIN market.instruments i ON i.id = t.instrument_id
            JOIN market.bar_coverage c ON c.instrument_id = i.id
            WHERE t.active AND i.retired_at IS NULL AND i.type IN ('share', 'etf', 'index') AND c.first_bar IS NOT NULL
            ORDER BY i.id
            """);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}

/// <summary>
/// Backfill is complete when a bar-history job is not running and every instrument the history phase fetches has stored bars (spec AU-12, ADR 0243 point 7). Anything else, including nothing tracked, is not complete: fail closed.
/// </summary>
internal sealed class SimAllBackfillGate(IFetchTargets targets, IDataJobStore jobs) : ISimAllBackfillGate
{
    public async ValueTask<string?> IncompleteReasonAsync(CancellationToken cancellationToken)
    {
        if (await jobs.GetRunningAsync(cancellationToken) is not null)
        {
            return "a bar-history job is running.";
        }

        var list = await targets.ListAsync(cancellationToken);
        if (list.Count == 0)
        {
            return "no instrument is tracked for history.";
        }

        var missing = list.Count(t => t.FirstBar is null);
        return missing == 0 ? null : $"{missing} of {list.Count} tracked instruments have no stored bars yet.";
    }

    public async ValueTask<bool> DataJobRunningAsync(CancellationToken cancellationToken) => await jobs.GetRunningAsync(cancellationToken) is not null;
}

/// <summary>Free space of the root volume, as the recorders measure it (the database volume is the same disk in the compose stack).</summary>
internal sealed class FreeDiskProbe : IFreeDiskProbe
{
    public long? FreeBytes()
    {
        try
        {
            return new DriveInfo("/").AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
