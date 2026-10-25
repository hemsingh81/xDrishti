using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The insert-only store behind <see cref="ITicketAlertStore"/> (spec P7-20, ADR 0115): raw SQL. It reads the plan record, the portfolio trades and the journal links, and inserts only into its own three tables, which the runtime role can
/// insert into and read but not change. A crossing or prompt that exists is skipped by the unique indexes (<c>ON CONFLICT DO NOTHING</c>). Nothing here changes a ticket, writes <c>plan_events</c> or talks to a broker.
/// </summary>
internal sealed class TicketAlertStore(NpgsqlDataSource dataSource) : ITicketAlertStore
{
    private const string ItemStateSql = "COALESCE((SELECT e.to_state FROM trading.plan_events e WHERE e.item_id = i.id ORDER BY e.id DESC LIMIT 1), 'proposed')";

    public async ValueTask<IReadOnlyList<WatchedTicket>> ListWatchedAsync(DateOnly session, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT i.id, i.plan_id, i.symbol, i.side, p.for_date, i.stop_level, i.stop_anchor, i.target_level
            FROM trading.plan_items i JOIN trading.plans p ON p.id = i.plan_id
            WHERE p.for_date = @d AND i.item_group <> 'rejected' AND {ItemStateSql} = 'armed' AND (i.stop_level IS NOT NULL OR i.target_level IS NOT NULL)
              AND NOT EXISTS (SELECT 1 FROM trading.ticket_notice_acks a WHERE a.item_id = i.id AND a.action = 'mute')
            ORDER BY i.item_rank, i.id
            """);
        select.Parameters.AddWithValue("d", NpgsqlDbType.Date, session);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<WatchedTicket>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new WatchedTicket(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetDecimal(7)));
        }

        return rows;
    }

    public async ValueTask<bool> AddCrossingAsync(NewCrossing crossing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crossing);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO trading.ticket_alerts (plan_id, item_id, session, kind, level, stop_anchor, price, tick_at)
            SELECT @plan, @item, @session, @kind, @level, @anchor, @price, @tick
            WHERE NOT EXISTS (SELECT 1 FROM trading.ticket_notice_acks a WHERE a.item_id = @item AND a.action = 'mute')
            ON CONFLICT (item_id, kind) DO NOTHING
            """);
        insert.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, crossing.PlanId);
        insert.Parameters.AddWithValue("item", NpgsqlDbType.Bigint, crossing.ItemId);
        insert.Parameters.AddWithValue("session", NpgsqlDbType.Date, crossing.Session);
        insert.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, crossing.Kind);
        insert.Parameters.AddWithValue("level", NpgsqlDbType.Numeric, crossing.Level);
        insert.Parameters.AddWithValue("anchor", NpgsqlDbType.Varchar, (object?)crossing.StopAnchor ?? DBNull.Value);
        insert.Parameters.AddWithValue("price", NpgsqlDbType.Numeric, crossing.Price);
        insert.Parameters.AddWithValue("tick", NpgsqlDbType.TimestampTz, crossing.TickAt);
        return await insert.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async ValueTask<IReadOnlyList<CrossingRow>> ListCrossingsAsync(DateOnly fromSession, long? planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT id, plan_id, item_id, session, kind, level, stop_anchor, price, tick_at FROM trading.ticket_alerts
            WHERE session >= @from AND (@plan::bigint IS NULL OR plan_id = @plan::bigint) ORDER BY id
            """);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, fromSession);
        select.Parameters.Add(new NpgsqlParameter("plan", NpgsqlDbType.Bigint) { Value = planId is { } p ? p : DBNull.Value });
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<CrossingRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CrossingRow(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetFieldValue<DateOnly>(3), reader.GetString(4), reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDecimal(7), reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<NewPrompt>> ListArmedOnAsync(DateOnly session, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT i.plan_id, i.id, al.account_id FROM trading.plan_items i
            JOIN trading.plans p ON p.id = i.plan_id
            JOIN trading.plan_item_allocations al ON al.item_id = i.id
            WHERE p.for_date = @d AND i.item_group <> 'rejected' AND EXISTS (SELECT 1 FROM trading.plan_events e WHERE e.item_id = i.id AND e.to_state = 'armed')
            ORDER BY i.id, al.account_id
            """);
        select.Parameters.AddWithValue("d", NpgsqlDbType.Date, session);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<NewPrompt>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new NewPrompt(reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2), session));
        }

        return rows;
    }

    public async ValueTask<int> AddPromptsAsync(IReadOnlyList<NewPrompt> prompts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        var stored = 0;
        foreach (var prompt in prompts)
        {
            await using var insert = dataSource.CreateCommand("INSERT INTO trading.ticket_prompts (plan_id, item_id, account_id, session) VALUES (@plan, @item, @account, @session) ON CONFLICT (item_id, account_id) DO NOTHING");
            insert.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, prompt.PlanId);
            insert.Parameters.AddWithValue("item", NpgsqlDbType.Bigint, prompt.ItemId);
            insert.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, prompt.AccountId);
            insert.Parameters.AddWithValue("session", NpgsqlDbType.Date, prompt.Session);
            stored += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        return stored;
    }

    public async ValueTask<IReadOnlyList<PromptRow>> ListPromptsAsync(DateOnly fromSession, long? planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT t.id, t.plan_id, t.item_id, t.account_id, t.session, i.symbol, i.side FROM trading.ticket_prompts t JOIN trading.plan_items i ON i.id = t.item_id
            WHERE t.session >= @from AND (@plan::bigint IS NULL OR t.plan_id = @plan::bigint) ORDER BY t.id
            """);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, fromSession);
        select.Parameters.Add(new NpgsqlParameter("plan", NpgsqlDbType.Bigint) { Value = planId is { } p ? p : DBNull.Value });
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<PromptRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PromptRow(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetGuid(3), reader.GetFieldValue<DateOnly>(4), reader.GetString(5), reader.GetString(6)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<PromptTrade>> TradesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT t.account_id, t.trading_symbol, t.side, t.trade_date,
                   (SELECT e.item_id FROM trading.journal_fills f JOIN trading.journal_entries e ON e.id = f.entry_id WHERE f.transaction_id = t.id AND f.role = 'entry' LIMIT 1)
            FROM portfolio.transactions t
            WHERE t.voided_at IS NULL AND t.trade_date BETWEEN @from AND @to AND t.account_id IN (SELECT p.account_id FROM trading.ticket_prompts p)
            """);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        select.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<PromptTrade>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PromptTrade(reader.GetGuid(0), TradeRules.NormaliseSymbol(reader.GetString(1)), reader.GetString(2) == "buy" ? Side.Buy : Side.Sell, reader.GetFieldValue<DateOnly>(3), reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<AckRow>> ListAcksAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, notice_kind, notice_id, item_id, action, until FROM trading.ticket_notice_acks ORDER BY id");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<AckRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AckRow(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return rows;
    }

    public async ValueTask AddAckAsync(NewAck ack, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ack);
        await using var insert = dataSource.CreateCommand("INSERT INTO trading.ticket_notice_acks (notice_kind, notice_id, item_id, action, until, actor) VALUES (@kind, @notice, @item, @action, @until, @actor)");
        insert.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, ack.NoticeKind);
        insert.Parameters.Add(new NpgsqlParameter("notice", NpgsqlDbType.Bigint) { Value = ack.NoticeId is { } n ? n : DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("item", NpgsqlDbType.Bigint) { Value = ack.ItemId is { } i ? i : DBNull.Value });
        insert.Parameters.AddWithValue("action", NpgsqlDbType.Varchar, ack.Action);
        insert.Parameters.Add(new NpgsqlParameter("until", NpgsqlDbType.TimestampTz) { Value = ack.Until is { } u ? u : DBNull.Value });
        insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, ack.Actor);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> NoticeExistsAsync(string noticeKind, long noticeId, CancellationToken cancellationToken)
    {
        var table = noticeKind switch
        {
            "crossing" => "trading.ticket_alerts",
            "prompt" => "trading.ticket_prompts",
            _ => throw new ArgumentOutOfRangeException(nameof(noticeKind)),
        };
        await using var select = dataSource.CreateCommand($"SELECT EXISTS (SELECT 1 FROM {table} WHERE id = @id)");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, noticeId);
        return (bool)(await select.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<ItemSession?> FindItemAsync(long itemId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT i.plan_id, p.for_date FROM trading.plan_items i JOIN trading.plans p ON p.id = i.plan_id WHERE i.id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, itemId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new ItemSession(reader.GetInt64(0), reader.GetFieldValue<DateOnly>(1)) : null;
    }
}
