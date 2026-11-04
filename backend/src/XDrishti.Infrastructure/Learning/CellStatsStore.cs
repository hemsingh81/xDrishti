using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.cell_stats</c> behind <see cref="ICellStatsStore"/> (spec P6-01, ADR 0064): raw SQL, insert and select only (the runtime role has no more). A snapshot is written in one transaction.</summary>
internal sealed class CellStatsStore(NpgsqlDataSource dataSource) : ICellStatsStore
{
    private const string Columns = """
        snapshot_key, created_at, half_life_days, prior_strength, min_trades, credible_level, sources, as_of, strategy, timeframe, side, style, exit_policy, regime, window_code, costed, n, wins, effective_n, win_rate_raw, prior_rate,
        win_rate_posterior, win_rate_lower, win_rate_upper, avg_win_r, avg_loss_r, expectancy_r, profit_factor, max_drawdown_r, mae_p50, mae_p90, mfe_p50, mfe_p90, enough
        """;

    // CF-01 (ADR 0203): the dependence columns, read after cell_key (ordinals 35 to 45); all null in a row written before the migration
    private const string DependenceColumns = "n_eff, design_effect, n_eff_method, needed_trades, min_detectable_edge_r, min_trl, enough_by_n_eff, span_years, n_eff_mode, n_eff_note, target_edge_r";

    // CF-02 (ADR 0204): the shrinkage columns, read after the dependence ones (ordinals 46 to 58); all null in a row written before the migration
    private const string ShrinkageColumns = "parent_level, parent_mean_r, tau_squared, blend, shrunk_mean_r, shrunk_lower_r, estimated_strength, shrunk_win_rate, tau_source, strength_source, shrink_n, shrink_mode, shrink_note";

    // P14-06 (ADR 0193): a cash key has exactly five separators (the six-part text every stored key has). The decision reads choose their snapshot among cash keys only, so a later snapshot that holds only option or pair cells cannot hide the cash snapshot.
    private const string CashKey = "(length(cell_key) - length(replace(cell_key, '|', '')) = 5)";

    private const string NewestCash = $"(SELECT snapshot_key FROM research.cell_stats WHERE {CashKey} ORDER BY as_of DESC, created_at DESC, id DESC LIMIT 1)";

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
                    sources, created_at, created_by, n_eff, design_effect, n_eff_method, needed_trades, min_detectable_edge_r, min_trl, enough_by_n_eff, span_years, n_eff_mode, n_eff_note, target_edge_r,
                    parent_level, parent_mean_r, tau_squared, blend, shrunk_mean_r, shrunk_lower_r, estimated_strength, shrunk_win_rate, tau_source, strength_source, shrink_n, shrink_mode, shrink_note)
                VALUES (@snapshot_key, @as_of, @cell_key, @strategy, @timeframe, @side, @style, @exit, @regime, @window, @costed, @n, @wins, @effective_n, @raw, @prior, @posterior,
                    @lower, @upper, @avg_win, @avg_loss, @expectancy, @pf, @dd, @mae50, @mae90, @mfe50, @mfe90, @enough, @half_life, @strength, @min_trades, @level,
                    @sources, @created_at, @created_by, @n_eff, @design_effect, @n_eff_method, @needed_trades, @min_detectable_edge_r, @min_trl, @enough_by_n_eff, @span_years, @n_eff_mode, @n_eff_note, @target_edge_r,
                    @parent_level, @parent_mean_r, @tau_squared, @blend, @shrunk_mean_r, @shrunk_lower_r, @estimated_strength, @shrunk_win_rate, @tau_source, @strength_source, @shrink_n, @shrink_mode, @shrink_note)
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
            var d = row.Dependence;
            Nullable(p, "n_eff", d?.NEff);
            Nullable(p, "design_effect", d?.DesignEffect);
            p.AddWithValue("n_eff_method", NpgsqlDbType.Text, d is null ? DBNull.Value : (object)d.Method);
            p.AddWithValue("needed_trades", NpgsqlDbType.Integer, d?.NeededTrades is { } needed ? (object)needed : DBNull.Value);
            Nullable(p, "min_detectable_edge_r", d?.MinDetectableEdgeR);
            p.AddWithValue("min_trl", NpgsqlDbType.Integer, d?.MinTrl is { } trl ? (object)trl : DBNull.Value);
            p.AddWithValue("enough_by_n_eff", NpgsqlDbType.Boolean, d?.EnoughByNEff is { } enoughBy ? (object)enoughBy : DBNull.Value);
            Nullable(p, "span_years", d?.SpanYears);
            p.AddWithValue("n_eff_mode", NpgsqlDbType.Text, d is null ? DBNull.Value : (object)d.Mode.ToString());
            Nullable(p, "target_edge_r", d?.TargetEdgeR);
            p.AddWithValue("n_eff_note", NpgsqlDbType.Text, d is null ? DBNull.Value : (object)d.Note);
            var sh = row.Shrinkage;
            Text(p, "parent_level", sh?.ParentLevel);
            Nullable(p, "parent_mean_r", sh?.ParentMeanR);
            Nullable(p, "tau_squared", sh?.TauSquared);
            Nullable(p, "blend", sh?.Blend);
            Nullable(p, "shrunk_mean_r", sh?.ShrunkMeanR);
            Nullable(p, "shrunk_lower_r", sh?.ShrunkLowerR);
            Nullable(p, "estimated_strength", sh?.EstimatedStrength);
            Nullable(p, "shrunk_win_rate", sh?.ShrunkWinRate);
            Text(p, "tau_source", sh?.TauSource);
            Text(p, "strength_source", sh?.StrengthSource);
            Nullable(p, "shrink_n", sh?.NUsed);
            Text(p, "shrink_mode", sh?.Mode.ToString());
            Text(p, "shrink_note", sh?.Note);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<IReadOnlyList<StoredCellStats>> ListLatestAsync(string? strategy, string? window, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns}, cell_key, {DependenceColumns}, {ShrinkageColumns} FROM research.cell_stats
            WHERE snapshot_key = {Newest} AND regime = '*' AND (@strategy::text IS NULL OR strategy = @strategy) AND (@window::text IS NULL OR window_code = @window)
            ORDER BY cell_key, window_code
            """);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Text, (object?)strategy ?? DBNull.Value);
        command.Parameters.AddWithValue("window", NpgsqlDbType.Text, (object?)window ?? DBNull.Value);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredCellStats>> ListLatestCashAsync(string? strategy, string? window, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns}, cell_key, {DependenceColumns}, {ShrinkageColumns} FROM research.cell_stats
            WHERE snapshot_key = {NewestCash} AND {CashKey} AND regime = '*' AND (@strategy::text IS NULL OR strategy = @strategy) AND (@window::text IS NULL OR window_code = @window)
            ORDER BY cell_key, window_code
            """);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Text, (object?)strategy ?? DBNull.Value);
        command.Parameters.AddWithValue("window", NpgsqlDbType.Text, (object?)window ?? DBNull.Value);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredCellStats>> ListRegimeLatestAsync(string? strategy, string? window, CancellationToken cancellationToken)
    {
        // P6-02 (ADR 0146): the newest snapshot that holds any regime row; the rows carry their own as-of date and snapshot key, so an older snapshot is visible as older
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns}, cell_key, {DependenceColumns}, {ShrinkageColumns} FROM research.cell_stats
            WHERE snapshot_key = (SELECT snapshot_key FROM research.cell_stats WHERE regime <> '*' ORDER BY as_of DESC, created_at DESC, id DESC LIMIT 1)
              AND regime <> '*' AND (@strategy::text IS NULL OR strategy = @strategy) AND (@window::text IS NULL OR window_code = @window)
            ORDER BY cell_key, window_code
            """);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Text, (object?)strategy ?? DBNull.Value);
        command.Parameters.AddWithValue("window", NpgsqlDbType.Text, (object?)window ?? DBNull.Value);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<DateOnly?> NewestAsOfAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT max(as_of) FROM research.cell_stats WHERE {CashKey}");
        return await command.ExecuteScalarAsync(cancellationToken) is DateOnly date ? date : null;
    }

    public async ValueTask<IReadOnlyList<StoredCellStats>> ListLatestBeforeAsync(DateOnly before, string? strategy, string? window, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns}, cell_key, {DependenceColumns}, {ShrinkageColumns} FROM research.cell_stats
            WHERE snapshot_key = (SELECT snapshot_key FROM research.cell_stats WHERE as_of < @before AND {CashKey} ORDER BY as_of DESC, created_at DESC, id DESC LIMIT 1)
              AND regime = '*' AND (@strategy::text IS NULL OR strategy = @strategy) AND (@window::text IS NULL OR window_code = @window)
            ORDER BY cell_key, window_code
            """);
        command.Parameters.AddWithValue("before", NpgsqlDbType.Date, before);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Text, (object?)strategy ?? DBNull.Value);
        command.Parameters.AddWithValue("window", NpgsqlDbType.Text, (object?)window ?? DBNull.Value);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredCellStats>> FindLatestAsync(string cellKey, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns}, cell_key, {DependenceColumns}, {ShrinkageColumns} FROM research.cell_stats
            WHERE snapshot_key = (SELECT snapshot_key FROM research.cell_stats WHERE cell_key = @key ORDER BY as_of DESC, created_at DESC, id DESC LIMIT 1) AND cell_key = @key
            ORDER BY window_code
            """);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, cellKey);
        return await ReadAsync(command, cancellationToken);
    }

    private static void Text(NpgsqlParameterCollection parameters, string name, string? value) =>
        parameters.AddWithValue(name, NpgsqlDbType.Text, value is null ? DBNull.Value : value);

    // a row written before CF-02 has no mode: "not computed" (null), never a number
    private static ShrinkageAssessment? ShrinkageOf(DbDataReader reader)
    {
        if (reader.IsDBNull(57) || !Enum.TryParse<ConfidenceMode>(reader.GetString(57), out var mode))
        {
            return null;
        }

        return new ShrinkageAssessment(
            mode,
            reader.IsDBNull(46) ? null : reader.GetString(46),
            Opt(reader, 47),
            Opt(reader, 48),
            reader.IsDBNull(54) ? null : reader.GetString(54),
            Opt(reader, 49),
            Opt(reader, 50),
            Opt(reader, 51),
            Opt(reader, 52),
            reader.IsDBNull(55) ? null : reader.GetString(55),
            Opt(reader, 53),
            Opt(reader, 56),
            reader.IsDBNull(58) ? string.Empty : reader.GetString(58));
    }

    private static void Nullable(NpgsqlParameterCollection parameters, string name, double? value) =>
        parameters.AddWithValue(name, NpgsqlDbType.Double, value.HasValue ? value.Value : DBNull.Value);

    // a row written before CF-01 has no method: "not computed" (null), never a number
    private static DependenceAssessment? DependenceOf(DbDataReader reader, int n)
    {
        if (reader.IsDBNull(37) || !Enum.TryParse<ConfidenceMode>(reader.IsDBNull(43) ? null : reader.GetString(43), out var mode))
        {
            return null;
        }

        var nEff = Opt(reader, 35);
        var de = Opt(reader, 36);
        int? needed = reader.IsDBNull(38) ? null : reader.GetInt32(38);
        int? trl = reader.IsDBNull(40) ? null : reader.GetInt32(40);
        var span = Opt(reader, 42);
        return new DependenceAssessment(
            mode,
            n,
            nEff,
            de,
            reader.GetString(37),
            needed,
            Opt(reader, 39),
            trl,
            trl.HasValue && de.HasValue ? (int)Math.Ceiling(Math.Round(trl.Value * de.Value, 9)) : null,
            span,
            nEff.HasValue && needed.HasValue && span.HasValue ? PowerAnalysis.YearsToEvidence(nEff.Value, span.Value, needed.Value) : null,
            reader.IsDBNull(41) ? null : reader.GetBoolean(41),
            n < 20,
            reader.IsDBNull(44) ? string.Empty : reader.GetString(44),
            Opt(reader, 45));
    }

    private static double? Opt(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static async ValueTask<IReadOnlyList<StoredCellStats>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<StoredCellStats>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var options = new CellStatsOptions(reader.GetDouble(2), reader.GetDouble(3), reader.GetInt32(4), reader.GetDouble(5));
            var cell = new CellId(reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13));
            var key = reader.GetString(34);
            if (key.Count(c => c == '|') > 5)
            {
                // P14-06: the columns hold six parts; the seventh and eighth are read from the key. A longer key that does not parse, or whose first six parts differ from the columns, is a corrupt row: never read as cash. A six-part key is untouched.
                var longer = CellId.TryParse(key);
                if (longer is null || longer.Strategy != cell.Strategy || longer.Timeframe != cell.Timeframe || longer.Side != cell.Side || longer.Style != cell.Style || longer.Exit != cell.Exit || longer.Regime != cell.Regime)
                {
                    throw new InvalidDataException("A stored cell row has a cell key that is not a valid key of its columns.");
                }

                cell = cell with { Universe = longer.Universe, Expression = longer.Expression };
            }

            var window = CellWindowExtensions.Parse(reader.GetString(14)) ?? throw new InvalidDataException("A stored cell row has an unknown window.");
            var row = new CellStatsRow(
                cell, window, reader.GetFieldValue<DateOnly>(7), reader.GetBoolean(15), reader.GetInt32(16), reader.GetInt32(17), reader.GetDouble(18), Opt(reader, 19), reader.GetDouble(20), reader.GetDouble(21), reader.GetDouble(22),
                reader.GetDouble(23), Opt(reader, 24), Opt(reader, 25), Opt(reader, 26), Opt(reader, 27), reader.GetDouble(28), Opt(reader, 29), Opt(reader, 30), Opt(reader, 31), Opt(reader, 32), reader.GetBoolean(33),
                DependenceOf(reader, reader.GetInt32(16)), ShrinkageOf(reader));
            list.Add(new StoredCellStats(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), options, reader.GetString(6), row));
        }

        return list;
    }
}
