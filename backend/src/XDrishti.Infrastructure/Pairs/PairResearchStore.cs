using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Infrastructure.Pairs;

/// <summary><c>research.pair_formations</c> and <c>research.pair_trades</c> behind <see cref="IPairResearchStore"/> (P14-13, ADR 0196): raw SQL, insert and select only (the runtime role has no more). The id and the key are the identity; a second insert writes nothing.</summary>
internal sealed class PairResearchStore(NpgsqlDataSource dataSource) : IPairResearchStore
{
    public async ValueTask<bool> AppendFormationAsync(PairFormationRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO research.pair_formations (formation_id, family, quarter, window_from, window_to, candidates, tested, unavailable, selected, detail)
            VALUES (@id, @family, @quarter, @from, @to, @candidates, @tested, @unavailable, @selected, @detail)
            ON CONFLICT (formation_id) DO NOTHING
            """);
        var p = command.Parameters;
        p.AddWithValue("id", NpgsqlDbType.Char, row.FormationId);
        p.AddWithValue("family", NpgsqlDbType.Varchar, row.Family);
        p.AddWithValue("quarter", NpgsqlDbType.Varchar, row.Quarter);
        p.AddWithValue("from", NpgsqlDbType.Date, row.WindowFrom);
        p.AddWithValue("to", NpgsqlDbType.Date, row.WindowTo);
        p.AddWithValue("candidates", NpgsqlDbType.Integer, row.Candidates);
        p.AddWithValue("tested", NpgsqlDbType.Integer, row.Tested);
        p.AddWithValue("unavailable", NpgsqlDbType.Integer, row.Unavailable);
        p.AddWithValue("selected", NpgsqlDbType.Integer, row.Selected);
        p.AddWithValue("detail", NpgsqlDbType.Jsonb, row.DetailJson);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<int> AppendTradesAsync(IReadOnlyList<PairTradeRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var written = 0;
        foreach (var row in rows)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO research.pair_trades (run_id, pair_key, ordinal, entry_ts, exit_ts, side, z_entry, z_exit, exit_reason, qty_y, qty_x, gross_inr, net_inr, r, labels)
                VALUES (@run, @pair, @ordinal, @entry, @exit, @side, @ze, @zx, @reason, @qy, @qx, @gross, @net, @r, @labels)
                ON CONFLICT (run_id, pair_key, ordinal) DO NOTHING
                """, connection, transaction);
            var p = command.Parameters;
            p.AddWithValue("run", NpgsqlDbType.Char, row.RunId);
            p.AddWithValue("pair", NpgsqlDbType.Varchar, row.PairKey);
            p.AddWithValue("ordinal", NpgsqlDbType.Integer, row.Ordinal);
            p.AddWithValue("entry", NpgsqlDbType.TimestampTz, row.EntryTs.ToUniversalTime());
            p.AddWithValue("exit", NpgsqlDbType.TimestampTz, row.ExitTs.ToUniversalTime());
            p.AddWithValue("side", NpgsqlDbType.Varchar, row.Side);
            p.AddWithValue("ze", NpgsqlDbType.Double, row.ZEntry);
            p.Add(new NpgsqlParameter("zx", NpgsqlDbType.Double) { Value = (object?)row.ZExit ?? DBNull.Value });
            p.AddWithValue("reason", NpgsqlDbType.Varchar, row.ExitReason);
            p.AddWithValue("qy", NpgsqlDbType.Integer, row.QtyY);
            p.AddWithValue("qx", NpgsqlDbType.Integer, row.QtyX);
            p.AddWithValue("gross", NpgsqlDbType.Numeric, row.GrossInr);
            p.Add(new NpgsqlParameter("net", NpgsqlDbType.Numeric) { Value = (object?)row.NetInr ?? DBNull.Value });
            p.AddWithValue("r", NpgsqlDbType.Double, row.R);
            p.AddWithValue("labels", NpgsqlDbType.Array | NpgsqlDbType.Text, row.Labels.ToArray());
            written += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    public async ValueTask<int> CountOtherTradesAsync(string cfgLabel, DateOnly before, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cfgLabel);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM research.pair_trades WHERE NOT (@cfg = ANY(labels)) AND (entry_ts AT TIME ZONE 'Asia/Kolkata')::date < @before");
        command.Parameters.AddWithValue("cfg", NpgsqlDbType.Text, cfgLabel);
        command.Parameters.AddWithValue("before", NpgsqlDbType.Date, before);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask<IReadOnlyList<PairStoredTrade>> ReadTradesAsync(string cfgLabel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cfgLabel);
        await using var command = dataSource.CreateCommand("SELECT run_id, pair_key, extract(epoch FROM entry_ts)::bigint, r, gross_inr, net_inr, labels FROM research.pair_trades WHERE @cfg = ANY(labels) ORDER BY entry_ts, pair_key, run_id, ordinal");
        command.Parameters.AddWithValue("cfg", NpgsqlDbType.Text, cfgLabel);
        var list = new List<PairStoredTrade>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var epoch = reader.GetInt64(2);
            list.Add(new PairStoredTrade(reader.GetString(0), reader.GetString(1), SessionClock.DateOf(epoch), epoch, reader.GetDouble(3), reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.GetFieldValue<string[]>(6)));
        }

        return list;
    }

    public async ValueTask<IReadOnlyList<StoredFormation>> ReadFormationsAsync(string family, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(family);
        await using var command = dataSource.CreateCommand("SELECT quarter, candidates, tested, unavailable, selected, detail::text, formation_id, created_at FROM research.pair_formations WHERE family = @family ORDER BY quarter, created_at, formation_id");
        command.Parameters.AddWithValue("family", NpgsqlDbType.Varchar, family);
        var list = new List<StoredFormation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StoredFormation(reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), new PairFormationStat(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), HalfLives(reader.GetString(5)))));
        }

        return list;
    }

    private static List<double> HalfLives(string detail)
    {
        var values = new List<double>();
        using var document = JsonDocument.Parse(detail);
        if (document.RootElement.TryGetProperty("half_lives", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.TryGetProperty("sessions", out var sessions) && sessions.ValueKind == JsonValueKind.Number)
                {
                    values.Add(sessions.GetDouble());
                }
            }
        }

        return values;
    }
}
