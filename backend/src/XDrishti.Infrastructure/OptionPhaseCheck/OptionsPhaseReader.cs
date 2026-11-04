using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionPhaseCheck;

namespace XDrishti.Infrastructure.OptionPhaseCheck;

/// <summary>
/// The select-only reads of the P14 phase check (spec P14-17, ADR 0202): the headers of expression runs by id, when fetch-log rows ended by id, and the plan items and paper entries whose cell key has an eighth part. Every query is bounded by an
/// id list or a limit and returns versions, ids, instants and words: no price, IV, Greek, bar, quote or return figure. The UTC instants come back as stored.
/// </summary>
internal sealed class OptionsPhaseReader(NpgsqlDataSource dataSource) : IOptionsPhaseReader
{
    public async ValueTask<IReadOnlyList<PhaseRunInputs>> RunInputsAsync(IReadOnlyCollection<string> runIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        Bound(runIds.Count);
        await using var command = dataSource.CreateCommand("SELECT run_id, remark_version, inputs::text FROM research.expression_runs WHERE run_id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Char, runIds.ToArray());
        var list = new List<PhaseRunInputs>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PhaseRunInputs(reader.GetString(0).Trim(), reader.GetString(1).Trim(), reader.GetString(2)));
        }

        return list;
    }

    public async ValueTask<IReadOnlyDictionary<long, DateTimeOffset>> FetchEndedAtAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Bound(ids.Count);
        await using var command = dataSource.CreateCommand("SELECT id, ended_at FROM options.fetch_log WHERE id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, ids.ToArray());
        var found = new Dictionary<long, DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found[reader.GetInt64(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        }

        return found;
    }

    // shortcut: max(ended_at) has no index (only started_at has one), so this scans the fetch log once per check; the log is a few hundred thousand rows at most.
    public async ValueTask<DateTimeOffset?> LatestFetchEndedAtAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT max(ended_at) FROM options.fetch_log WHERE outcome IN ('ok', 'empty', 'no_data')");
        return await command.ExecuteScalarAsync(cancellationToken) is DateTimeOffset at ? at : null;
    }

    public async ValueTask<bool> IsChainRecorderOnAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT enabled FROM options.chain_recorder_switch ORDER BY id DESC LIMIT 1");
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask<bool> IsBackfillOnAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT enabled FROM options.backfill_switch ORDER BY id DESC LIMIT 1");
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask<IReadOnlyList<NonCashCellRow>> NonCashCellRowsAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        // shortcut: the scan is unindexed (no index on cell_key's parts); both tables are small (one row per ticket or capture) and each read stops at its first rows. See ADR 0202.
        // A canonical cell key has six parts, or seven with a universe, or eight with a non-cash expression (P14-06): seven separators or more is the eighth part. Each table is read to its first rows only.
        await using var command = dataSource.CreateCommand(
            """
            (SELECT 'trading.plan_items' AS source, id FROM trading.plan_items WHERE cardinality(string_to_array(cell_key, '|')) >= 8 AND split_part(cell_key, '|', 8) <> 'cash' ORDER BY id LIMIT @limit)
            UNION ALL
            (SELECT 'paper.entries' AS source, id FROM paper.entries WHERE cardinality(string_to_array(cell_key, '|')) >= 8 AND split_part(cell_key, '|', 8) <> 'cash' ORDER BY id LIMIT @limit)
            ORDER BY source, id
            LIMIT @limit
            """);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var rows = new List<NonCashCellRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new NonCashCellRow(reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }

    private static void Bound(int count) => ArgumentOutOfRangeException.ThrowIfGreaterThan(count, IOptionsPhaseReader.MaxIds);
}
