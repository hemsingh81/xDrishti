using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The insert-only run and break record as the role xd_exec (P12-06), and the read of the owner's resolutions (written by xd-api; xd_exec has SELECT only). A run and its new breaks are written in one transaction. Nothing updates or deletes, and the database refuses it anyway.
/// </summary>
internal sealed class PgReconStore(NpgsqlDataSource data) : IReconStore
{
    public async Task<long> RecordRunAsync(Guid accountId, ReconKind kind, bool readable, int orders, int trades, int positions, int breakCount, IReadOnlyList<ReconBreakDraft> newBreaks, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newBreaks);
        var session = DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var run = new NpgsqlCommand(
            "INSERT INTO execution.recon_runs (at, account_id, session_date, kind, readable, orders, trades, positions, breaks) VALUES (@at, @account, @session, @kind, @readable, @orders, @trades, @positions, @breaks) RETURNING id", connection, tx);
        run.Parameters.AddWithValue("at", at);
        run.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = accountId });
        run.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Date) { Value = session });
        run.Parameters.AddWithValue("kind", kind.ToString().ToLowerInvariant());
        run.Parameters.AddWithValue("readable", readable);
        run.Parameters.AddWithValue("orders", orders);
        run.Parameters.AddWithValue("trades", trades);
        run.Parameters.AddWithValue("positions", positions);
        run.Parameters.AddWithValue("breaks", breakCount);
        var runId = (long)(await run.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        foreach (var b in newBreaks)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO execution.recon_breaks (run_id, account_id, session_date, kind, severity, symbol, intent_id, key, ours, theirs, detail, at) VALUES (@run, @account, @session, @kind, @severity, @symbol, @intent, @key, @ours, @theirs, @detail, @at)", connection, tx);
            insert.Parameters.AddWithValue("run", runId);
            insert.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = accountId });
            insert.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Date) { Value = session });
            insert.Parameters.AddWithValue("kind", BreakKinds.Code(b.Kind));
            insert.Parameters.AddWithValue("severity", BreakKinds.Severity(b.Kind));
            insert.Parameters.Add(new NpgsqlParameter("symbol", NpgsqlDbType.Varchar) { Value = (object?)Cut(b.Symbol, 32) ?? DBNull.Value });
            insert.Parameters.Add(new NpgsqlParameter("intent", NpgsqlDbType.Bigint) { Value = (object?)b.IntentId ?? DBNull.Value });
            insert.Parameters.AddWithValue("key", Cut(b.Key, 160)!);
            insert.Parameters.Add(new NpgsqlParameter("ours", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(Cut(b.Ours, 200)) });
            insert.Parameters.Add(new NpgsqlParameter("theirs", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(Cut(b.Theirs, 200)) });
            insert.Parameters.AddWithValue("detail", Cut(b.Detail, 300)!);
            insert.Parameters.AddWithValue("at", at);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return runId;
    }

    public async Task<IReadOnlySet<string>> ResolvedKeysAsync(Guid accountId, DateOnly sessionDate, CancellationToken cancellationToken) =>
        await KeysAsync("SELECT b.key FROM execution.recon_breaks b JOIN execution.break_resolutions r ON r.break_id = b.id WHERE b.account_id = @account AND b.session_date = @session", accountId, sessionDate, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlySet<string>> OpenKeysAsync(Guid accountId, CancellationToken cancellationToken) =>
        await KeysAsync("SELECT b.key FROM execution.recon_breaks b WHERE b.account_id = @account AND NOT EXISTS (SELECT 1 FROM execution.break_resolutions r WHERE r.break_id = b.id)", accountId, null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StoredBreak>> BreaksAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(
            """
            SELECT b.id, b.run_id, b.account_id, b.kind, b.key, b.symbol, b.intent_id, b.at, r.at, r.resolved_by, r.resolution
            FROM execution.recon_breaks b LEFT JOIN execution.break_resolutions r ON r.break_id = b.id
            WHERE (@account IS NULL OR b.account_id = @account) ORDER BY b.id
            """);
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
        List<StoredBreak> rows = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new StoredBreak(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2), BreakKinds.Parse(reader.GetString(3)) ?? BreakKind.Unavailable, reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : new ResolutionRecord(reader.GetFieldValue<DateTimeOffset>(8), reader.GetString(9), reader.GetString(10))));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ReconRunRecord>> RunsAsync(Guid accountId, DateOnly sessionDate, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT id, at, account_id, kind, readable, breaks FROM execution.recon_runs WHERE account_id = @account AND session_date = @session ORDER BY id");
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = accountId });
        command.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Date) { Value = sessionDate });
        List<ReconRunRecord> rows = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ReconRunRecord(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetGuid(2), Enum.Parse<ReconKind>(reader.GetString(3), ignoreCase: true), reader.GetBoolean(4), reader.GetInt32(5)));
        }

        return rows;
    }

    private async Task<IReadOnlySet<string>> KeysAsync(string sql, Guid accountId, DateOnly? session, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = accountId });
        if (session is { } day)
        {
            command.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Date) { Value = day });
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    private static string? Cut(string? text, int max) => text is null ? null : text.Length > max ? text[..max] : text;
}

/// <summary>
/// Our side of the books for one account, read-only (P12-06): the legs, fills, positions and intents of the account's latest session. There is no write in this class. A leg status text that is not one of ours makes the read fail (unavailable), never a guess.
/// </summary>
internal sealed class PgReconOurSource(NpgsqlDataSource data) : IReconOurSource
{
    private const string Latest = "i.session_date = (SELECT max(x.session_date) FROM execution.intents x WHERE x.account_id = @account)";

    public async Task<IReadOnlyList<Guid>> AccountsAsync(CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT DISTINCT account_id FROM execution.intents ORDER BY 1");
        List<Guid> ids = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    public async Task<OurAccountView> LoadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        List<OurLegView> legs = [];
        await using (var command = Command($"SELECT l.intent_id, l.client_order_id, l.broker_order_id, l.kind, l.status, l.qty, l.filled_qty, i.security_id, i.side, COALESCE(l.last_update_at, l.send_started_at, i.created_at), l.leg_no FROM execution.legs l JOIN execution.intents i ON i.id = l.intent_id WHERE i.account_id = @account AND {Latest} AND l.client_order_id IS NOT NULL", accountId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                legs.Add(new OurLegView(
                    reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), Kind(reader.GetString(3)), Status(reader.GetString(4)), reader.GetInt32(5), reader.GetInt32(6),
                    reader.GetString(7), Side(reader.GetString(8)), reader.GetFieldValue<DateTimeOffset>(9), reader.IsDBNull(10) ? null : reader.GetInt16(10)));
            }
        }

        List<OurFillView> fills = [];
        await using (var command = Command($"SELECT f.exchange_trade_id, l.client_order_id, f.qty, f.price, i.security_id, i.side FROM execution.fills f JOIN execution.legs l ON l.id = f.leg_id JOIN execution.intents i ON i.id = f.intent_id WHERE i.account_id = @account AND {Latest}", accountId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                fills.Add(new OurFillView(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetDecimal(3), reader.GetString(4), Side(reader.GetString(5))));
            }
        }

        List<OurPositionView> positions = [];
        await using (var command = Command($"SELECT DISTINCT i.security_id, p.net_qty FROM execution.positions p JOIN execution.intents i ON i.account_id = p.account_id AND i.symbol = p.symbol WHERE p.account_id = @account AND {Latest}", accountId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                positions.Add(new OurPositionView(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        List<OurIntentView> intents = [];
        await using (var command = Command($"SELECT i.id, i.account_id, s.state, i.security_id, i.side, i.quantity, s.filled_qty, i.stop_price, i.entry_price FROM execution.intents i JOIN execution.intent_state s ON s.intent_id = i.id WHERE i.account_id = @account AND {Latest}", accountId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                intents.Add(new OurIntentView(reader.GetInt64(0), reader.GetGuid(1), Enum.Parse<Domain.ExecState>(reader.GetString(2), ignoreCase: true), reader.GetString(3), Side(reader.GetString(4)), reader.GetInt32(5), reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8)));
            }
        }

        return new OurAccountView(legs, fills, positions, intents);
    }

    private NpgsqlCommand Command(string sql, Guid accountId)
    {
        var command = data.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = accountId });
        return command;
    }

    private static Domain.LegKind Kind(string code) => code switch
    {
        "E" => Domain.LegKind.Entry,
        "S" => Domain.LegKind.Stop,
        "T" => Domain.LegKind.Target,
        "X" => Domain.LegKind.Exit,
        "F" => Domain.LegKind.Flatten,
        _ => throw new InvalidOperationException("unknown leg kind"),
    };

    private static Domain.LegStatus Status(string text) =>
        Enum.TryParse<Domain.LegStatus>(text.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var status) ? status : throw new InvalidOperationException("unknown leg status");

    private static Domain.ExecSide Side(string text) => text.ToUpperInvariant() switch
    {
        "BUY" => Domain.ExecSide.Buy,
        "SELL" => Domain.ExecSide.Sell,
        _ => throw new InvalidOperationException("unknown side"),
    };
}

/// <summary>Writes the authoritative fill rows (P12-06): one row per exchange trade id, idempotent, as the role xd_exec (INSERT on <c>execution.fills</c>).</summary>
internal sealed class PgFillWriter(NpgsqlDataSource data) : IFillWriter
{
    public async Task<bool> RecordAsync(FillRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var command = data.CreateCommand(
            """
            INSERT INTO execution.fills (leg_id, intent_id, exchange_trade_id, qty, price, at, source)
            SELECT l.id, l.intent_id, @trade, @qty, @price, @at, 'broker_poll' FROM execution.legs l WHERE l.client_order_id = @cid
            ON CONFLICT (exchange_trade_id) DO NOTHING
            """);
        command.Parameters.AddWithValue("trade", row.ExchangeTradeId);
        command.Parameters.AddWithValue("qty", row.Qty);
        command.Parameters.Add(new NpgsqlParameter("price", NpgsqlDbType.Numeric) { Value = Math.Round(row.Price, 4) });
        command.Parameters.AddWithValue("at", row.At);
        command.Parameters.AddWithValue("cid", row.ClientOrderId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }
}
