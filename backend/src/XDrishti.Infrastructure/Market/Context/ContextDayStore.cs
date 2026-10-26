using Npgsql;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Infrastructure.Market.Context;

/// <summary>The stored days of the context (<c>market.context_daily</c>), insert-only: <c>ON CONFLICT DO NOTHING</c>, so a stored day is never touched. A few rows a day, so plain inserts, no COPY.</summary>
internal sealed class ContextDayStore(NpgsqlDataSource dataSource, TimeProvider clock) : IContextDayStore
{
    /// <summary>The <c>source</c> of a row derived from the stored 1-minute bars.</summary>
    public const string DerivedSource = "derived_1m";

    public async ValueTask<ContextStamp> StampAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT count(*)::integer, max(date), count(breadth_state)::integer, max(breadth_computed_at), coalesce(sum(breadth), 0)::double precision FROM market.context_daily");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ContextStamp(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetDouble(4));
    }

    public async ValueTask<IReadOnlyList<DateOnly>> ListDatesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT date FROM market.context_daily ORDER BY date");
        var dates = new List<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return dates;
    }

    public async ValueTask<IReadOnlyList<ContextDayRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT date, index_high, index_low, index_close, vix_close, index_bars, vix_bars, breadth, breadth_universe FROM market.context_daily ORDER BY date");
        var rows = new List<ContextDayRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ContextDayRecord(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.GetInt16(5),
                reader.IsDBNull(6) ? null : reader.GetInt16(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetInt16(8)));
        }

        return rows;
    }

    public async ValueTask<int> InsertAsync(Guid indexInstrumentId, IReadOnlyList<ContextDayRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var inserted = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var record in records)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO market.context_daily (date, index_instrument_id, index_high, index_low, index_close, vix_close, index_bars, vix_bars, source, written_at)
                VALUES (@date, @instrument, @high, @low, @close, @vix, @indexBars, @vixBars, @source, @now)
                ON CONFLICT (date) DO NOTHING
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("date", record.Date);
            command.Parameters.AddWithValue("instrument", indexInstrumentId);
            command.Parameters.AddWithValue("high", record.IndexHigh);
            command.Parameters.AddWithValue("low", record.IndexLow);
            command.Parameters.AddWithValue("close", record.IndexClose);
            command.Parameters.Add(new NpgsqlParameter<double?>("vix", record.VixClose));
            command.Parameters.AddWithValue("indexBars", (short)record.IndexBars);
            command.Parameters.Add(new NpgsqlParameter<short?>("vixBars", record.VixBars is { } vb ? (short)vb : null));
            command.Parameters.AddWithValue("source", DerivedSource);
            command.Parameters.AddWithValue("now", now);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async ValueTask<IReadOnlyList<StoredBreadth>> ListBreadthAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT date, breadth, breadth_voters, breadth_universe, breadth_excluded, breadth_state FROM market.context_daily ORDER BY date");
        var rows = new List<StoredBreadth>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StoredBreadth(
                reader.GetFieldValue<DateOnly>(0),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetInt16(2),
                reader.IsDBNull(3) ? null : reader.GetInt16(3),
                reader.IsDBNull(4) ? null : reader.GetInt16(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    /// <summary>Set once: <c>WHERE breadth_state IS NULL</c>, or an <c>insufficient</c> one when the write allows it (a day inside the grace after a late load of stock bars).</summary>
    public ValueTask<int> SetBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken) =>
        UpdateAsync(writes, "WHERE date = @date AND (breadth_state IS NULL OR (@replace AND breadth_state = 'insufficient'))", cancellationToken);

    /// <summary>The explicit rebuild: the one write that replaces a stored breadth.</summary>
    public ValueTask<int> ReplaceBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken) =>
        UpdateAsync(writes, "WHERE date = @date AND @replace", cancellationToken);

    private async ValueTask<int> UpdateAsync(IReadOnlyList<BreadthWrite> writes, string where, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (writes.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var write in writes)
        {
            var command = new NpgsqlBatchCommand(
                "UPDATE market.context_daily SET breadth = @breadth, breadth_voters = @voters, breadth_universe = @universe, breadth_excluded = @excluded, breadth_state = @state, breadth_computed_at = @now " + where);
            command.Parameters.Add(new NpgsqlParameter<double?>("breadth", write.Result.Value));
            command.Parameters.AddWithValue("voters", (short)write.Result.Voters);
            command.Parameters.AddWithValue("universe", (short)write.Result.Universe);
            command.Parameters.AddWithValue("excluded", (short)write.Result.Excluded);
            command.Parameters.AddWithValue("state", write.Result.State);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("date", write.Date);
            command.Parameters.AddWithValue("replace", write.ReplaceInsufficient);
            batch.BatchCommands.Add(command);
        }

        var changed = await batch.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }
}
