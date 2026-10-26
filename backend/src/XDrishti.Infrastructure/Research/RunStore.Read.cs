using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>The read path of <see cref="RunStore"/> (spec P3-10, D9): the header, the list and the record text through EF projections without tracking; the trades through a plain reader (a 217,000-row run must not pass the change tracker).</summary>
internal sealed partial class RunStore
{
    private static readonly string TradeSelect = "SELECT open_at_end, ordinal, " + string.Join(", ", TradeColumns.Columns.Select(c => c.Name)) + " FROM research.sim_trades";

    public async ValueTask<StoredRunHeader?> FindAsync(string runKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        return await Headers(db.SimRuns.AsNoTracking().Where(x => x.RunKey == runKey)).FirstOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<string?> ReadRecordTextAsync(string runKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        return await db.SimRuns.AsNoTracking().Where(x => x.RunKey == runKey).Select(x => x.RecordJson).FirstOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<Result<Page<StoredRunHeader>>> ListAsync(RunListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);
        if (!page.IsSizeValid)
        {
            return Result.Failure<Page<StoredRunHeader>>(StoreErrors.PageInvalid($"The page size is 1 to {PageRequest.MaxSize}."));
        }

        var query = db.SimRuns.AsNoTracking().AsQueryable();
        if (filter.StrategyId is { } strategy)
        {
            query = query.Where(x => x.StrategyId == strategy);
        }

        if (filter.EngineVersion is { } engine)
        {
            query = query.Where(x => x.EngineVersion == engine);
        }

        if (filter.Costed is { } costed)
        {
            query = costed ? query.Where(x => x.CostsSheetId != null) : query.Where(x => x.CostsSheetId == null);
        }

        if (filter.CreatedFrom is { } from)
        {
            query = query.Where(x => x.CreatedAt >= from);
        }

        if (filter.CreatedTo is { } to)
        {
            query = query.Where(x => x.CreatedAt < to);
        }

        if (page.Cursor is not null)
        {
            if (!PageCursor.TryReadRun(page.Cursor, out var createdAt, out var id))
            {
                return Result.Failure<Page<StoredRunHeader>>(StoreErrors.PageInvalid("The cursor is not valid."));
            }

            query = query.Where(x => x.CreatedAt < createdAt || (x.CreatedAt == createdAt && x.Id < id));
        }

        var rows = await Headers(query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(page.Size + 1)).ToListAsync(cancellationToken);
        var more = rows.Count > page.Size;
        var items = more ? rows.GetRange(0, page.Size) : rows;
        return Result.Success(new Page<StoredRunHeader>(items, more ? PageCursor.ForRun(items[^1].CreatedAt, items[^1].Id) : null));
    }

    public async IAsyncEnumerable<RunTrade> StreamTradesAsync(string runKey, bool openAtEnd, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        if (await FindRunAsync(runKey, cancellationToken) is not { } run)
        {
            yield break;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(TradeSelect + " WHERE run_id = @run_id AND open_at_end = @open_at_end ORDER BY ordinal", connection);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Bigint, run.Id);
        command.Parameters.AddWithValue("open_at_end", NpgsqlDbType.Boolean, openAtEnd);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await HasLegsAsync(run.Id, cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                yield return ReadTrade(reader, run);
            }

            yield break;
        }

        // P4-05, D9: a run with legs reads them in chunks beside the trades, from tables keyed like the trades
        var chunk = new List<(bool OpenAtEnd, int Ordinal, RunTrade Trade)>(LegChunk);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunk.Add((reader.GetBoolean(0), reader.GetInt32(1), ReadTrade(reader, run)));
            if (chunk.Count == LegChunk)
            {
                foreach (var trade in await WithLegsAsync(run.Id, chunk, cancellationToken))
                {
                    yield return trade;
                }

                chunk.Clear();
            }
        }

        if (chunk.Count > 0)
        {
            foreach (var trade in await WithLegsAsync(run.Id, chunk, cancellationToken))
            {
                yield return trade;
            }
        }
    }

    public async ValueTask<Result<Page<RunTrade>>> PageTradesAsync(string runKey, TradePageFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);
        if (!page.IsSizeValid)
        {
            return Result.Failure<Page<RunTrade>>(StoreErrors.PageInvalid($"The page size is 1 to {PageRequest.MaxSize}."));
        }

        var cursorOpen = false;
        var cursorOrdinal = 0;
        if (page.Cursor is not null && !PageCursor.TryReadTrade(page.Cursor, out cursorOpen, out cursorOrdinal))
        {
            return Result.Failure<Page<RunTrade>>(StoreErrors.PageInvalid("The cursor is not valid."));
        }

        if (await FindRunAsync(runKey, cancellationToken) is not { } run)
        {
            return Result.Failure<Page<RunTrade>>(Error.NotFound("store.run_not_found", "No run with this key is stored."));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand { Connection = connection };
        var sql = new StringBuilder(TradeSelect).Append(" WHERE run_id = @run_id");
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Bigint, run.Id);
        Equal(sql, command, "side", filter.Side is { } side ? EnumText<Side>.Name(side) : null);
        Equal(sql, command, "symbol", filter.Symbol);
        Equal(sql, command, "exit_reason", filter.ExitReason is { } reason ? EnumText<ExitReason>.Name(reason) : null);
        if (filter.TradeDateFrom is { } from)
        {
            sql.Append(" AND trade_date >= @date_from");
            command.Parameters.AddWithValue("date_from", NpgsqlDbType.Date, from);
        }

        if (filter.TradeDateTo is { } to)
        {
            sql.Append(" AND trade_date <= @date_to");
            command.Parameters.AddWithValue("date_to", NpgsqlDbType.Date, to);
        }

        if (filter.OpenAtEnd is { } open)
        {
            sql.Append(" AND open_at_end = @filter_open_at_end");
            command.Parameters.AddWithValue("filter_open_at_end", NpgsqlDbType.Boolean, open);
        }

        if (page.Cursor is not null)
        {
            sql.Append(" AND (open_at_end, ordinal) > (@cursor_open_at_end, @cursor_ordinal)");
            command.Parameters.AddWithValue("cursor_open_at_end", NpgsqlDbType.Boolean, cursorOpen);
            command.Parameters.AddWithValue("cursor_ordinal", NpgsqlDbType.Integer, cursorOrdinal);
        }

        sql.Append(" ORDER BY open_at_end, ordinal LIMIT @limit");
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, page.Size + 1);
        command.CommandText = sql.ToString();

        var chunk = new List<(bool OpenAtEnd, int Ordinal, RunTrade Trade)>(page.Size);
        (bool OpenAtEnd, int Ordinal) last = default;
        var more = false;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (chunk.Count == page.Size)
                {
                    more = true;
                    break;
                }

                last = (reader.GetBoolean(0), reader.GetInt32(1));
                chunk.Add((last.OpenAtEnd, last.Ordinal, ReadTrade(reader, run)));
            }
        }
        catch (StoreReadException unreadable)
        {
            return Result.Failure<Page<RunTrade>>(StoreErrors.RowUnreadable(unreadable.Message));
        }

        // the page is read to the end before the legs are, on a connection of their own; a run with no leg skips the second read
        var items = chunk.Count > 0 && await HasLegsAsync(run.Id, cancellationToken)
            ? await WithLegsAsync(run.Id, chunk, cancellationToken)
            : [.. chunk.Select(c => c.Trade)];
        return Result.Success(new Page<RunTrade>(items, more ? PageCursor.ForTrade(last.OpenAtEnd, last.Ordinal) : null));
    }

    public async ValueTask<RunTrade?> ReadTradeAsync(string runKey, bool openAtEnd, int ordinal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        if (await FindRunAsync(runKey, cancellationToken) is not { } run)
        {
            return null;
        }

        var chunk = new List<(bool OpenAtEnd, int Ordinal, RunTrade Trade)>(1);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(TradeSelect + " WHERE run_id = @run_id AND open_at_end = @open_at_end AND ordinal = @ordinal", connection))
        {
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Bigint, run.Id);
            command.Parameters.AddWithValue("open_at_end", NpgsqlDbType.Boolean, openAtEnd);
            command.Parameters.AddWithValue("ordinal", NpgsqlDbType.Integer, ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                chunk.Add((reader.GetBoolean(0), reader.GetInt32(1), ReadTrade(reader, run)));
            }
        }

        if (chunk.Count == 0)
        {
            return null;
        }

        var items = await HasLegsAsync(run.Id, cancellationToken) ? await WithLegsAsync(run.Id, chunk, cancellationToken) : [chunk[0].Trade];
        return items[0];
    }

    private static void Equal(StringBuilder sql, NpgsqlCommand command, string column, string? value)
    {
        if (value is null)
        {
            return;
        }

        sql.Append(" AND ").Append(column).Append(" = @").Append(column);
        command.Parameters.AddWithValue(column, NpgsqlDbType.Text, value);
    }

    private static IQueryable<StoredRunHeader> Headers(IQueryable<SimRunRow> rows) =>
        rows.Select(x => new StoredRunHeader(
            x.Id, x.RunKey, x.EngineVersion, x.RecordSchemaVersion, x.TradeSchemaVersion, x.StrategyId, x.DefinitionSha256, x.FromDate, x.ToDate, x.UniverseKind, x.UniverseLabel,
            x.Instruments, x.InstrumentsRun, x.TradesInWindow, x.CandidatesInWindow, x.TradeCount, x.OpenAtEndCount, x.AcknowledgeCsvAdjusted, x.Fills, x.CostsSheetId, x.CostsRatesSha256,
            x.RecordJson.Length, x.RecordSha256, x.TradesSha256, x.CreatedAt, x.CreatedBy));

    private async Task<RunInfo?> FindRunAsync(string runKey, CancellationToken cancellationToken)
    {
        var found = await db.SimRuns.AsNoTracking().Where(x => x.RunKey == runKey).Select(x => new { x.Id, x.CostsSheetId, x.TradeSchemaVersion }).FirstOrDefaultAsync(cancellationToken);
        return found is null ? null : new RunInfo(found.Id, found.CostsSheetId, found.TradeSchemaVersion);
    }

    /// <summary>One row of a reader as a <see cref="RunTrade"/>: the values by the mapping table, the schema and the sheet id from the run.</summary>
    private static RunTrade ReadTrade(NpgsqlDataReader reader, RunInfo run)
    {
        // Schema 4 (sim-5) trades have no legs and equal their schema-5 form in every other field (P4-01, D8): both are read.
        if (run.TradeSchemaVersion is not (SimTrade.CurrentSchemaVersion or SimTrade.CurrentSchemaVersion - 1))
        {
            throw new StoreReadException($"The trades of this run are of schema {run.TradeSchemaVersion}; this code reads schemas {SimTrade.CurrentSchemaVersion - 1} and {SimTrade.CurrentSchemaVersion}.");
        }

        var columns = TradeColumns.Columns;
        var values = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            values[i] = columns[i].Read(reader, i + 2);
        }

        return TradeColumns.Materialise(values, run.CostsSheetId);
    }

    private sealed record RunInfo(long Id, string? CostsSheetId, short TradeSchemaVersion);
}
