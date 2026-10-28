using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>
/// The execution feedback record (<c>trading.live_executions</c>, <c>live_order_classes</c>, <c>slippage_estimates</c>, <c>capacity_scores</c>, spec P11-02, ADR 0150): insert-only for the runtime role. Writes use <c>ON CONFLICT DO NOTHING</c> against the unique keys,
/// so a row seen again is not stored again and the count of new rows is exact (one snapshot per week and key). The tables check the same rules again.
/// </summary>
internal sealed class LiveExecutionStore(NpgsqlDataSource dataSource) : ILiveExecutionStore
{
    private const string ExecutionColumns = """
        id, outcome_id, account_id, item_id, for_date, symbol, side, style, product, bucket, adv_turnover_cr, participation_pct, tick, reference_kind, reference_price, risk_per_share, arrival_price, fill_vwap, fill_qty,
        slip_total, slip_delay, slip_exec, slip_total_ticks, slip_total_bps, slip_total_r, slip_delay_r, slip_exec_r, entry_delay_seconds, plan_entry, plan_exit, plan_stop, plan_charges_r, exit_vwap, exit_qty, live_charges, computed_at
        """;

    public async ValueTask<int> InsertExecutionsAsync(IReadOnlyList<NewLiveExecution> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var added = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var e in rows)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO trading.live_executions (outcome_id, account_id, item_id, for_date, symbol, side, style, product, bucket, adv_turnover_cr, participation_pct, tick, reference_kind, reference_price, risk_per_share, arrival_price, fill_vwap, fill_qty,
                    slip_total, slip_delay, slip_exec, slip_total_ticks, slip_total_bps, slip_total_r, slip_delay_r, slip_exec_r, entry_delay_seconds, plan_entry, plan_exit, plan_stop, plan_charges_r, exit_vwap, exit_qty, live_charges)
                VALUES (@outcome, @account, @item, @date, @symbol, @side, @style, @product, @bucket, @adv, @part, @tick, @kind, @ref, @risk, @arrival, @vwap, @qty,
                    @st, @sd, @se, @stt, @stb, @str, @sdr, @ser, @delay, @pe, @px, @ps, @pc, @xv, @xq, @lc)
                ON CONFLICT (outcome_id) DO NOTHING
                """, connection);
            command.Parameters.AddWithValue("outcome", e.OutcomeId);
            command.Parameters.AddWithValue("account", e.AccountId);
            command.Parameters.AddWithValue("item", e.ItemId);
            command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = e.ForDate });
            command.Parameters.AddWithValue("symbol", e.Symbol);
            command.Parameters.AddWithValue("side", e.Side);
            command.Parameters.AddWithValue("style", e.Style);
            command.Parameters.AddWithValue("product", e.Product);
            command.Parameters.AddWithValue("bucket", e.Bucket);
            command.Parameters.Add(Dec("adv", e.AdvTurnoverCr));
            command.Parameters.Add(Dec("part", e.ParticipationPct));
            command.Parameters.Add(Dec("tick", e.Tick));
            command.Parameters.AddWithValue("kind", e.ReferenceKind);
            command.Parameters.Add(Dec("ref", e.ReferencePrice));
            command.Parameters.Add(Dec("risk", e.RiskPerShare));
            command.Parameters.Add(Dec("arrival", e.ArrivalPrice));
            command.Parameters.Add(Dec("vwap", e.FillVwap));
            command.Parameters.AddWithValue("qty", e.FillQty);
            command.Parameters.Add(Dbl("st", e.SlipTotal));
            command.Parameters.Add(Dbl("sd", e.SlipDelay));
            command.Parameters.Add(Dbl("se", e.SlipExec));
            command.Parameters.Add(Dbl("stt", e.SlipTotalTicks));
            command.Parameters.Add(Dbl("stb", e.SlipTotalBps));
            command.Parameters.Add(Dbl("str", e.SlipTotalR));
            command.Parameters.Add(Dbl("sdr", e.SlipDelayR));
            command.Parameters.Add(Dbl("ser", e.SlipExecR));
            command.Parameters.Add(new NpgsqlParameter("delay", NpgsqlDbType.Integer) { Value = (object?)e.EntryDelaySeconds ?? DBNull.Value });
            command.Parameters.Add(Dec("pe", e.PlanEntry));
            command.Parameters.Add(Dec("px", e.PlanExit));
            command.Parameters.Add(Dec("ps", e.PlanStop));
            command.Parameters.Add(Dbl("pc", e.PlanChargesR));
            command.Parameters.Add(Dec("xv", e.ExitVwap));
            command.Parameters.Add(new NpgsqlParameter("xq", NpgsqlDbType.Bigint) { Value = (object?)e.ExitQty ?? DBNull.Value });
            command.Parameters.Add(Dec("lc", e.LiveCharges));
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    public async ValueTask<IReadOnlySet<long>> ExecutionOutcomeIdsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT outcome_id FROM trading.live_executions WHERE account_id = @account AND for_date = @date");
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        var ids = new HashSet<long>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            ids.Add(r.GetInt64(0));
        }

        return ids;
    }

    public async ValueTask<int> InsertOrderClassesAsync(IReadOnlyList<LiveOrderClassRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var added = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var c in rows)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO trading.live_order_classes (account_id, broker_order_id, outcome_id, for_date, symbol, bucket, order_type, qty, filled_qty, class)
                VALUES (@account, @order, @outcome, @date, @symbol, @bucket, @type, @qty, @filled, @class)
                ON CONFLICT (account_id, broker_order_id) DO NOTHING
                """, connection);
            command.Parameters.AddWithValue("account", c.AccountId);
            command.Parameters.AddWithValue("order", c.BrokerOrderId);
            command.Parameters.AddWithValue("outcome", c.OutcomeId);
            command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = c.ForDate });
            command.Parameters.AddWithValue("symbol", c.Symbol);
            command.Parameters.AddWithValue("bucket", c.Bucket);
            command.Parameters.AddWithValue("type", c.OrderType);
            command.Parameters.AddWithValue("qty", c.Qty);
            command.Parameters.AddWithValue("filled", c.FilledQty);
            command.Parameters.AddWithValue("class", c.Class);
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    public async ValueTask<IReadOnlySet<string>> ClassifiedOrderIdsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT broker_order_id FROM trading.live_order_classes WHERE account_id = @account AND for_date = @date");
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        var ids = new HashSet<string>(StringComparer.Ordinal);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            ids.Add(r.GetString(0));
        }

        return ids;
    }

    public async ValueTask<StoredExecutionPage> ListExecutionsAsync(Guid? accountId, DateOnly from, DateOnly to, int skip, int take, CancellationToken cancellationToken)
    {
        await using var count = dataSource.CreateCommand("SELECT count(*) FROM trading.live_executions WHERE (@account::uuid IS NULL OR account_id = @account) AND for_date BETWEEN @from AND @to");
        Bind(count, accountId, from, to);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        await using var command = dataSource.CreateCommand($"SELECT {ExecutionColumns} FROM trading.live_executions WHERE (@account::uuid IS NULL OR account_id = @account) AND for_date BETWEEN @from AND @to ORDER BY for_date DESC, id DESC OFFSET @skip LIMIT @take");
        Bind(command, accountId, from, to);
        command.Parameters.AddWithValue("skip", skip);
        command.Parameters.AddWithValue("take", take);
        return new StoredExecutionPage(await ReadExecutionsAsync(command, cancellationToken), total);
    }

    public async ValueTask<StoredLiveExecution?> FindExecutionAsync(long id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {ExecutionColumns} FROM trading.live_executions WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var rows = await ReadExecutionsAsync(command, cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    // shortcut: unbounded read of every row up to the date; fine at tens of rows a month, page or aggregate in SQL if it ever grows.
    public async ValueTask<IReadOnlyList<StoredLiveExecution>> ExecutionsUntilAsync(DateOnly until, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {ExecutionColumns} FROM trading.live_executions WHERE for_date <= @until ORDER BY for_date, id");
        command.Parameters.Add(new NpgsqlParameter("until", NpgsqlDbType.Date) { Value = until });
        return await ReadExecutionsAsync(command, cancellationToken);
    }

    // shortcut: unbounded read of every row up to the date; fine at tens of rows a month, page or aggregate in SQL if it ever grows.
    public async ValueTask<IReadOnlyList<LiveOrderClassRow>> OrderClassesUntilAsync(DateOnly until, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT account_id, broker_order_id, outcome_id, for_date, symbol, bucket, order_type, qty, filled_qty, class FROM trading.live_order_classes WHERE for_date <= @until ORDER BY for_date, id");
        command.Parameters.Add(new NpgsqlParameter("until", NpgsqlDbType.Date) { Value = until });
        var rows = new List<LiveOrderClassRow>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new LiveOrderClassRow(r.GetGuid(0), r.GetString(1), r.GetInt64(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt64(7), r.GetInt64(8), r.GetString(9)));
        }

        return rows;
    }

    public async ValueTask<(int Count, DateOnly? First, DateOnly? Last)> ExecutionSummaryAsync(DateOnly until, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(*), min(for_date), max(for_date) FROM trading.live_executions WHERE for_date <= @until");
        command.Parameters.Add(new NpgsqlParameter("until", NpgsqlDbType.Date) { Value = until });
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        return ((int)r.GetInt64(0), r.IsDBNull(1) ? null : r.GetFieldValue<DateOnly>(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateOnly>(2));
    }

    public async ValueTask<int> InsertSlippageEstimatesAsync(IReadOnlyList<NewSlippageEstimate> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var added = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var s in rows)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO trading.slippage_estimates (week_start, bucket, side, style, reference_kind, n, state, reason, mean_bps, mean_lo, mean_hi, median_bps, median_lo, median_hi, mean_ticks, mean_r, proposal, input_hash)
                VALUES (@week, @bucket, @side, @style, @kind, @n, @state, @reason, @mean, @mlo, @mhi, @med, @dlo, @dhi, @ticks, @r, @proposal, @hash)
                ON CONFLICT (week_start, bucket, side, style, reference_kind) DO NOTHING
                """, connection);
            command.Parameters.Add(new NpgsqlParameter("week", NpgsqlDbType.Date) { Value = s.WeekStart });
            command.Parameters.AddWithValue("bucket", s.Bucket);
            command.Parameters.AddWithValue("side", s.Side);
            command.Parameters.AddWithValue("style", s.Style);
            command.Parameters.AddWithValue("kind", s.ReferenceKind);
            command.Parameters.AddWithValue("n", s.N);
            command.Parameters.AddWithValue("state", s.State);
            command.Parameters.Add(Text("reason", s.Reason));
            command.Parameters.Add(Dbl("mean", s.MeanBps));
            command.Parameters.Add(Dbl("mlo", s.MeanLo));
            command.Parameters.Add(Dbl("mhi", s.MeanHi));
            command.Parameters.Add(Dbl("med", s.MedianBps));
            command.Parameters.Add(Dbl("dlo", s.MedianLo));
            command.Parameters.Add(Dbl("dhi", s.MedianHi));
            command.Parameters.Add(Dbl("ticks", s.MeanTicks));
            command.Parameters.Add(Dbl("r", s.MeanR));
            command.Parameters.Add(Text("proposal", s.Proposal));
            command.Parameters.AddWithValue("hash", s.InputHash);
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    public async ValueTask<int> InsertCapacityScoresAsync(IReadOnlyList<NewCapacityScore> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var added = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var c in rows)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO trading.capacity_scores (week_start, scope, scope_key, n_orders, n_full, n_partial, n_rejected, n_queue_miss, n_price_miss, n_unclassified, score, lo, hi, state, reason, prior_strength, input_hash)
                VALUES (@week, @scope, @key, @n, @full, @partial, @rejected, @queue, @price, @other, @score, @lo, @hi, @state, @reason, @strength, @hash)
                ON CONFLICT (week_start, scope, scope_key) DO NOTHING
                """, connection);
            command.Parameters.Add(new NpgsqlParameter("week", NpgsqlDbType.Date) { Value = c.WeekStart });
            command.Parameters.AddWithValue("scope", c.Scope);
            command.Parameters.AddWithValue("key", c.ScopeKey);
            command.Parameters.AddWithValue("n", c.NOrders);
            command.Parameters.AddWithValue("full", c.NFull);
            command.Parameters.AddWithValue("partial", c.NPartial);
            command.Parameters.AddWithValue("rejected", c.NRejected);
            command.Parameters.AddWithValue("queue", c.NQueueMiss);
            command.Parameters.AddWithValue("price", c.NPriceMiss);
            command.Parameters.AddWithValue("other", c.NUnclassified);
            command.Parameters.Add(Dbl("score", c.Score));
            command.Parameters.Add(Dbl("lo", c.Lo));
            command.Parameters.Add(Dbl("hi", c.Hi));
            command.Parameters.AddWithValue("state", c.State);
            command.Parameters.Add(Text("reason", c.Reason));
            command.Parameters.AddWithValue("strength", c.PriorStrength);
            command.Parameters.AddWithValue("hash", c.InputHash);
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    public async ValueTask<(DateOnly? Week, IReadOnlyList<NewSlippageEstimate> Rows)> SlippageEstimatesAsync(DateOnly? weekStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT week_start, bucket, side, style, reference_kind, n, state, reason, mean_bps, mean_lo, mean_hi, median_bps, median_lo, median_hi, mean_ticks, mean_r, proposal, input_hash
            FROM trading.slippage_estimates
            WHERE week_start = COALESCE(@week::date, (SELECT max(week_start) FROM trading.slippage_estimates))
            ORDER BY bucket, side, style, reference_kind
            """);
        command.Parameters.Add(new NpgsqlParameter("week", NpgsqlDbType.Date) { Value = weekStart is { } w ? w : DBNull.Value });
        var rows = new List<NewSlippageEstimate>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new NewSlippageEstimate(
                r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetString(6), NullableText(r, 7),
                NullableDouble(r, 8), NullableDouble(r, 9), NullableDouble(r, 10), NullableDouble(r, 11), NullableDouble(r, 12), NullableDouble(r, 13), NullableDouble(r, 14), NullableDouble(r, 15), NullableText(r, 16), r.GetString(17)));
        }

        return (rows.Count > 0 ? rows[0].WeekStart : null, rows);
    }

    public async ValueTask<(DateOnly? Week, IReadOnlyList<NewCapacityScore> Rows)> CapacityScoresAsync(DateOnly? weekStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT week_start, scope, scope_key, n_orders, n_full, n_partial, n_rejected, n_queue_miss, n_price_miss, n_unclassified, score, lo, hi, state, reason, prior_strength, input_hash
            FROM trading.capacity_scores
            WHERE week_start = COALESCE(@week::date, (SELECT max(week_start) FROM trading.capacity_scores))
            ORDER BY scope, scope_key
            """);
        command.Parameters.Add(new NpgsqlParameter("week", NpgsqlDbType.Date) { Value = weekStart is { } w ? w : DBNull.Value });
        var rows = new List<NewCapacityScore>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new NewCapacityScore(
                r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9),
                NullableDouble(r, 10), NullableDouble(r, 11), NullableDouble(r, 12), r.GetString(13), NullableText(r, 14), r.GetInt32(15), r.GetString(16)));
        }

        return (rows.Count > 0 ? rows[0].WeekStart : null, rows);
    }

    private static async Task<IReadOnlyList<StoredLiveExecution>> ReadExecutionsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<StoredLiveExecution>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var e = new NewLiveExecution(
                r.GetInt64(1), r.GetGuid(2), r.GetInt64(3), r.GetFieldValue<DateOnly>(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9),
                NullableDecimal(r, 10), NullableDecimal(r, 11), NullableDecimal(r, 12), r.GetString(13), NullableDecimal(r, 14), NullableDecimal(r, 15), NullableDecimal(r, 16), r.GetDecimal(17), r.GetInt64(18),
                NullableDouble(r, 19), NullableDouble(r, 20), NullableDouble(r, 21), NullableDouble(r, 22), NullableDouble(r, 23), NullableDouble(r, 24), NullableDouble(r, 25), NullableDouble(r, 26),
                r.IsDBNull(27) ? null : r.GetInt32(27), NullableDecimal(r, 28), NullableDecimal(r, 29), NullableDecimal(r, 30), r.GetDouble(31), NullableDecimal(r, 32), r.IsDBNull(33) ? null : r.GetInt64(33), NullableDecimal(r, 34));
            rows.Add(new StoredLiveExecution(r.GetInt64(0), e, Utc(r.GetDateTime(35))));
        }

        return rows;
    }

    private static void Bind(NpgsqlCommand command, Guid? accountId, DateOnly from, DateOnly to)
    {
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static decimal? NullableDecimal(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);

    private static double? NullableDouble(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static string? NullableText(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static NpgsqlParameter Dec(string name, decimal? value) => new(name, NpgsqlDbType.Numeric) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Dbl(string name, double? value) => new(name, NpgsqlDbType.Double) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlDbType.Varchar) { Value = (object?)value ?? DBNull.Value };
}
