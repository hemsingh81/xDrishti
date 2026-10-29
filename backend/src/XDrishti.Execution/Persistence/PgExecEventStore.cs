using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Domain.Common;
using XDrishti.Execution.Application;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The database event store (P12-01b), used as the role xd_exec. One transaction per event: the intent's state row is locked, the Domain folds the stored events and judges the next one, then the event is inserted; the database trigger judges it again
/// (chain, legal pair, cancel after a fill, fill above the quantity) and moves <c>intent_state</c>. Nothing is ever updated or deleted here except through those triggers.
/// </summary>
internal sealed class PgExecEventStore(NpgsqlDataSource data) : IExecEventStore
{
    private const string FillKey = "fill_qty";

    public async Task<Result> StartAsync(ExecIntent intent, DateOnly sessionDate, ExecEnvironment environment, string createdBy, CancellationToken cancellationToken)
    {
        if (environment == ExecEnvironment.None)
        {
            return Result.Failure(Error.Validation("execution.gate_closed", "An intent is created only in the sandbox or live environment."));
        }

        await using var command = data.CreateCommand("""
            INSERT INTO execution.intents (id, public_id, account_id, plan_item_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by)
            VALUES (@id, @public_id, @account_id, @plan_item_id, @session_date, @symbol, @security_id, @side, @quantity, @entry, @stop, @target, @structure, @environment, @created_by)
            """);
        command.Parameters.AddWithValue("id", intent.Id);
        command.Parameters.AddWithValue("public_id", intent.PublicId);
        command.Parameters.AddWithValue("account_id", intent.AccountId);
        command.Parameters.AddWithValue("plan_item_id", intent.PlanItemId);
        command.Parameters.AddWithValue("session_date", sessionDate);
        command.Parameters.AddWithValue("symbol", intent.Symbol);
        command.Parameters.AddWithValue("security_id", intent.SecurityId);
        command.Parameters.AddWithValue("side", intent.Side == ExecSide.Buy ? "buy" : "sell");
        command.Parameters.AddWithValue("quantity", intent.Qty);
        command.Parameters.AddWithValue("entry", intent.Entry);
        command.Parameters.AddWithValue("stop", intent.Stop);
        command.Parameters.AddWithValue("target", intent.Target);
        command.Parameters.AddWithValue("structure", intent.Structure == ExecStructure.Super ? "super" : "plain_stop");
        command.Parameters.AddWithValue("environment", environment == ExecEnvironment.Live ? "live" : "sandbox");
        command.Parameters.AddWithValue("created_by", createdBy);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result.Success();
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return Result.Failure(Error.Conflict("execution.event_conflict", "The intent already exists, or names an unknown account."));
        }
    }

    public async Task<Result> AppendAsync(long intentId, ExecEvent e, CancellationToken cancellationToken)
    {
        await using var connection = await data.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM execution.intent_state WHERE intent_id = @id FOR UPDATE", connection, tx))
        {
            lockRow.Parameters.AddWithValue("id", intentId);
            if (await lockRow.ExecuteScalarAsync(cancellationToken) is null)
            {
                return Result.Failure(Error.NotFound("execution.intent_unknown", "No such intent."));
            }
        }

        var folded = await FoldAsync(connection, tx, intentId, cancellationToken);
        if (folded.IsFailure)
        {
            return Result.Failure(folded.Error);
        }

        var next = folded.Value.Intent.Apply(e);
        if (next.IsFailure)
        {
            return Result.Failure(next.Error);
        }

        var payload = Payload(e);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO execution.events (intent_id, seq, kind, from_state, to_state, prev_event_id, source, actor, payload, at)
            VALUES (@intent_id, @seq, @kind, @from, @to, @prev, @source, @actor, @payload, @at)
            """, connection, tx);
        insert.Parameters.AddWithValue("intent_id", intentId);
        insert.Parameters.AddWithValue("seq", e.Seq);
        insert.Parameters.AddWithValue("kind", KindName(e.Kind));
        insert.Parameters.AddWithValue("from", (object?)StateName(e.From) ?? DBNull.Value);
        insert.Parameters.AddWithValue("to", (object?)StateName(e.To) ?? DBNull.Value);
        insert.Parameters.Add(new NpgsqlParameter("prev", NpgsqlDbType.Bigint) { Value = (object?)folded.Value.LastEventId ?? DBNull.Value });
        insert.Parameters.AddWithValue("source", SourceName(e.Source));
        insert.Parameters.AddWithValue("actor", e.Actor);
        insert.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
        insert.Parameters.AddWithValue("at", e.At);
        try
        {
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            // The transaction is rolled back by disposing it: a refused event stores nothing.
            var code = failure.MessageText.StartsWith(ExecLifecycle.IllegalMoveCode, StringComparison.Ordinal) ? ExecLifecycle.IllegalMoveCode : "execution.event_conflict";
            return Result.Failure(Error.Conflict(code, "The database refused the event."));
        }
    }

    public async Task<ExecIntentState?> StateAsync(long intentId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT state, seq, filled_qty FROM execution.intent_state WHERE intent_id = @id");
        command.Parameters.AddWithValue("id", intentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new ExecIntentState(ParseState(reader.GetString(0)), reader.GetInt32(1), reader.GetInt32(2)) : null;
    }

    public async Task<IReadOnlyList<ExecEvent>> EventsAsync(long intentId, CancellationToken cancellationToken)
    {
        await using var connection = await data.OpenConnectionAsync(cancellationToken);
        return (await ReadEventsAsync(connection, null, intentId, cancellationToken)).Select(r => r.Event).ToList();
    }

    public async Task<Result<ExecIntent>> RebuildAsync(long intentId, CancellationToken cancellationToken)
    {
        await using var connection = await data.OpenConnectionAsync(cancellationToken);
        var folded = await FoldAsync(connection, null, intentId, cancellationToken);
        return folded.IsFailure ? Result.Failure<ExecIntent>(folded.Error) : Result.Success(folded.Value.Intent);
    }

    private sealed record Folded(ExecIntent Intent, long? LastEventId);

    private static async Task<Result<Folded>> FoldAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, long intentId, CancellationToken cancellationToken)
    {
        var seed = await ReadIntentAsync(connection, tx, intentId, cancellationToken);
        if (seed is null)
        {
            return Result.Failure<Folded>(Error.NotFound("execution.intent_unknown", "No such intent."));
        }

        var state = seed;
        long? last = null;
        foreach (var (id, e) in await ReadEventsAsync(connection, tx, intentId, cancellationToken))
        {
            var next = state.Apply(e);
            if (next.IsFailure)
            {
                return Result.Failure<Folded>(next.Error);
            }

            state = next.Value;
            last = id;
        }

        return Result.Success(new Folded(state, last));
    }

    private static async Task<ExecIntent?> ReadIntentAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, long intentId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT public_id, account_id, plan_item_id, side, symbol, security_id, quantity, entry_price, stop_price, target_price, structure FROM execution.intents WHERE id = @id", connection, tx);
        command.Parameters.AddWithValue("id", intentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var created = ExecIntent.Create(
            intentId, reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetString(3) == "buy" ? ExecSide.Buy : ExecSide.Sell, reader.GetString(4), reader.GetString(5), reader.GetInt32(6),
            reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetString(10) == "super" ? ExecStructure.Super : ExecStructure.PlainStop);
        return created.IsSuccess ? created.Value : null;
    }

    private static async Task<List<(long Id, ExecEvent Event)>> ReadEventsAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, long intentId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT id, seq, kind, from_state, to_state, source, actor, at, payload::text FROM execution.events WHERE intent_id = @id ORDER BY seq", connection, tx);
        command.Parameters.AddWithValue("id", intentId);
        var events = new List<(long, ExecEvent)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = JsonNode.Parse(reader.GetString(8)) as JsonObject ?? [];
            var fill = payload.Remove(FillKey, out var node) && node is not null ? node.GetValue<int>() : 0;
            events.Add((reader.GetInt64(0), new ExecEvent(
                reader.GetInt32(1), ParseKind(reader.GetString(2)), reader.IsDBNull(3) ? null : ParseState(reader.GetString(3)), reader.IsDBNull(4) ? null : ParseState(reader.GetString(4)),
                ParseSource(reader.GetString(5)), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), fill, payload.ToJsonString())));
        }

        return events;
    }

    /// <summary>The payload text plus, for a fill, the filled quantity under <c>fill_qty</c> (the table has no quantity column; the database check reads it from there).</summary>
    private static string Payload(ExecEvent e)
    {
        var payload = JsonNode.Parse(string.IsNullOrWhiteSpace(e.Payload) ? "{}" : e.Payload) as JsonObject ?? [];
        if (e.Kind == ExecEventKind.Fill)
        {
            payload[FillKey] = e.FillQty;
        }

        return payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static string? StateName(ExecState? state) => state?.ToString().ToLowerInvariant();

    private static ExecState ParseState(string name) => Enum.Parse<ExecState>(name, ignoreCase: true);

    private static string KindName(ExecEventKind kind) => Snake(kind.ToString());

    private static string SourceName(ExecEventSource source) => Snake(source.ToString());

    private static ExecEventKind ParseKind(string name) => Enum.Parse<ExecEventKind>(name.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true);

    private static ExecEventSource ParseSource(string name) => Enum.Parse<ExecEventSource>(name.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true);

    private static string Snake(string pascal) => string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
