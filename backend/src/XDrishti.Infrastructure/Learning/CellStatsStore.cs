using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.cell_stats</c> behind <see cref="ICellStatsStore"/> (spec P6-01, ADR 0064): raw SQL, insert and select only (the runtime role has no more). A snapshot is written in one transaction.</summary>
internal sealed class CellStatsStore(NpgsqlDataSource dataSource) : ICellStatsStore
{
    private const string Columns = """
        snapshot_key, created_at, half_life_days, prior_strength, min_trades, credible_level, sources, as_of, strategy, timeframe, side, style, exit_policy, regime, window_code, costed, n, wins, effective_n, win_rate_raw, prior_rate,
        win_rate_posterior, win_rate_lower, win_rate_upper, avg_win_r, avg_loss_r, expectancy_r, profit_factor, max_drawdown_r, mae_p50, mae_p90, mfe_p50, mfe_p90, enough
        """;

    private const string Newest = "(SELECT snapshot_key FROM research.cell_stats ORDER BY as_of DESC, created_at DESC, id DESC LIMIT 1)";

    public async ValueTask<bool> StoreAsync(CellSnapshotToStore snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Text, snapshot.SnapshotKey);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var exists = new NpgsqlCommand("SELECT 1 FROM research.cell_stats WHERE snapshot_key = @k LIMIT 1", connection, transaction))
        {
            exists.Parameters.AddWithValue("k", NpgsqlDbType.Char, snapshot.SnapshotKey);
            if (await exists.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return false;
            }
        }

        foreach (var row in snapshot.Rows)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO research.cell_stats (snapshot_key, as_of, cell_key, strategy, timeframe, side, style, exit_policy, regime, window_code, costed, n, wins, effective_n, win_rate_raw, prior_rate, win_rate_posterior,
                    win_rate_lower, win_rate_upper, avg_win_r, avg_loss_r, expectancy_r, profit_factor, max_drawdown_r, mae_p50, mae_p90, mfe_p50, mfe_p90, enough, half_life_days, prior_strength, min_trades, credible_level,
                    sources, created_at, created_by)
                VALUES (@snapshot_key, @as_of, @cell_key, @strategy, @timeframe, @side, @style, @exit, @regime, @window, @costed, @n, @wins, @effective_n, @raw, @prior, @posterior,
                    @lower, @upper, @avg_win, @avg_loss, @expectancy, @pf, @dd, @mae50, @mae90, @mfe50, @mfe90, @enough, @half_life, @strength, @min_trades, @level,
                    @sources, @created_at, @created_by)
                ON CONFLICT (snapshot_key, cell_key, window_code) DO NOTHING
                """, connection, transaction);
            var p = insert.Parameters;
            p.AddWithValue("snapshot_key", NpgsqlDbType.Char, snapshot.SnapshotKey);
            p.AddWithValue("as_of", NpgsqlDbType.Date, row.AsOf);
            p.AddWithValue("cell_key", NpgsqlDbType.Text, row.Cell.Key);
            p.AddWithValue("strategy", NpgsqlDbType.Varchar, row.Cell.Strategy);
            p.AddWithValue("timeframe", NpgsqlDbType.Varchar, row.Cell.Timeframe);
            p.AddWithValue("side", NpgsqlDbType.Varchar, row.Cell.Side);
            p.AddWithValue("style", NpgsqlDbType.Varchar, row.Cell.Style);
            p.AddWithValue("exit", NpgsqlDbType.Varchar, row.Cell.Exit);
            p.AddWithValue("regime", NpgsqlDbType.Varchar, row.Cell.Regime);
            p.AddWithValue("window", NpgsqlDbType.Varchar, row.Window.Code());
            p.AddWithValue("costed", NpgsqlDbType.Boolean, row.Costed);
            p.AddWithValue("n", NpgsqlDbType.Integer, row.N);
            p.AddWithValue("wins", NpgsqlDbType.Integer, row.Wins);
            p.AddWithValue("effective_n", NpgsqlDbType.Double, row.EffectiveN);
            Nullable(p, "raw", row.WinRateRaw);
            p.AddWithValue("prior", NpgsqlDbType.Double, row.PriorRate);
            p.AddWithValue("posterior", NpgsqlDbType.Double, row.WinRatePosterior);
            p.AddWithValue("lower", NpgsqlDbType.Double, row.WinRateLower);
            p.AddWithValue("upper", NpgsqlDbType.Double, row.WinRateUpper);
            Nullable(p, "avg_win", row.AvgWinR);
            Nullable(p, "avg_loss", row.AvgLossR);
            Nullable(p, "expectancy", row.ExpectancyR);
            Nullable(p, "pf", row.ProfitFactor);
            p.AddWithValue("dd", NpgsqlDbType.Double, row.MaxDrawdownR);
            Nullable(p, "mae50", row.MaeP50);
            Nullable(p, "mae90", row.MaeP90);
            Nullable(p, "mfe50", row.MfeP50);
            Nullable(p, "mfe90", row.MfeP90);
            p.AddWithValue("enough", NpgsqlDbType.Boolean, row.Enough);
            p.AddWithValue("half_life", NpgsqlDbType.Double, snapshot.Options.RecencyHalfLifeDays);
            p.AddWithValue("strength", NpgsqlDbType.Double, snapshot.Options.PriorStrength);
            p.AddWithValue("min_trades", NpgsqlDbType.Integer, snapshot.Options.MinTrades);
            p.AddWithValue("level", NpgsqlDbType.Double, snapshot.Options.CredibleLevel);
            p.AddWithValue("sources", NpgsqlDbType.Text, snapshot.Sources);
            p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, snapshot.CreatedAt);
            p.AddWithValue("created_by", NpgsqlDbType.Varchar, snapshot.CreatedBy);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<IReadOnlyList<StoredCellStats>> ListLatestAsync(string? strategy, string? window, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns}, cell_key FROM research.cell_stats
            WHERE snapshot_key = {Newest} AND (@strategy::text IS NULL OR strategy = @strategy) AND (@window::text IS NULL OR window_code = @window)
            ORDER BY cell_key, window_code
            """);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Text, (object?)strategy ?? DBNull.Value);
        command.Parameters.AddWithValue("window", NpgsqlDbType.Text, (object?)window ?? DBNull.Value);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredCellStats>> FindLatestAsync(string cellKey, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns}, cell_key FROM research.cell_stats
            WHERE snapshot_key = (SELECT snapshot_key FROM research.cell_stats WHERE cell_key = @key ORDER BY as_of DESC, created_at DESC, id DESC LIMIT 1) AND cell_key = @key
            ORDER BY window_code
            """);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, cellKey);
        return await ReadAsync(command, cancellationToken);
    }

    private static void Nullable(NpgsqlParameterCollection parameters, string name, double? value) =>
        parameters.AddWithValue(name, NpgsqlDbType.Double, value.HasValue ? value.Value : DBNull.Value);

    private static double? Opt(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static async ValueTask<IReadOnlyList<StoredCellStats>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<StoredCellStats>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var options = new CellStatsOptions(reader.GetDouble(2), reader.GetDouble(3), reader.GetInt32(4), reader.GetDouble(5));
            var cell = new CellId(reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13));
            var window = CellWindowExtensions.Parse(reader.GetString(14)) ?? throw new InvalidDataException("A stored cell row has an unknown window.");
            var row = new CellStatsRow(
                cell, window, reader.GetFieldValue<DateOnly>(7), reader.GetBoolean(15), reader.GetInt32(16), reader.GetInt32(17), reader.GetDouble(18), Opt(reader, 19), reader.GetDouble(20), reader.GetDouble(21), reader.GetDouble(22),
                reader.GetDouble(23), Opt(reader, 24), Opt(reader, 25), Opt(reader, 26), Opt(reader, 27), reader.GetDouble(28), Opt(reader, 29), Opt(reader, 30), Opt(reader, 31), Opt(reader, 32), reader.GetBoolean(33));
            list.Add(new StoredCellStats(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), options, reader.GetString(6), row));
        }

        return list;
    }
}
