using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Persistence;

/// <summary>The insert-only check record (P12-03), as the role xd_exec: a run and all its results in one transaction, a confirmation, and the two reads the pre-send step needs. Nothing here updates or deletes.</summary>
internal sealed class PgCheckStore(NpgsqlDataSource data) : ICheckStore
{
    public async Task<long> AppendRunAsync(long intentId, CheckRun run, CancellationToken cancellationToken)
    {
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        long id;
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO execution.check_runs (kind, intent_id, at, verdict, settings_hash, inputs_hash, valid_until) VALUES (@kind, @intent, @at, @verdict, @sh, @ih, @valid) RETURNING id", connection, tx))
        {
            insert.Parameters.AddWithValue("kind", run.Kind.ToString().ToLowerInvariant());
            insert.Parameters.AddWithValue("intent", intentId);
            insert.Parameters.AddWithValue("at", run.At);
            insert.Parameters.AddWithValue("verdict", VerdictText(run.Verdict));
            insert.Parameters.AddWithValue("sh", run.SettingsHash);
            insert.Parameters.AddWithValue("ih", run.InputsHash);
            insert.Parameters.AddWithValue("valid", run.ValidUntil);
            id = (long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        short ord = 0;
        foreach (var r in run.Results)
        {
            await using var row = new NpgsqlCommand(
                "INSERT INTO execution.check_results (run_id, ord, code, class, outcome, value, limit_value, detail, input_age_ms, source) VALUES (@run, @ord, @code, @class, @outcome, @value, @limit, @detail, @age, @source)", connection, tx);
            row.Parameters.AddWithValue("run", id);
            row.Parameters.AddWithValue("ord", ord++);
            row.Parameters.AddWithValue("code", r.Code);
            row.Parameters.AddWithValue("class", r.Class.ToString().ToLowerInvariant());
            row.Parameters.AddWithValue("outcome", r.Outcome.ToString().ToLowerInvariant());
            row.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Double) { Value = Finite(r.Value) });
            row.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Double) { Value = Finite(r.Limit) });
            row.Parameters.AddWithValue("detail", r.Detail.Length > 500 ? r.Detail[..500] : r.Detail);
            row.Parameters.Add(new NpgsqlParameter("age", NpgsqlDbType.Bigint) { Value = (object?)r.InputAgeMs ?? DBNull.Value });
            row.Parameters.AddWithValue("source", r.Source);
            await row.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (run.Confirmation is { } confirmation)
        {
            await InsertConfirmationAsync(connection, tx, id, confirmation, cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task<StoredRun?> LatestFullRunAsync(long intentId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT id, at, valid_until, verdict FROM execution.check_runs WHERE intent_id = @intent AND kind = 'full' ORDER BY id DESC LIMIT 1");
        command.Parameters.AddWithValue("intent", intentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredRun(reader.GetInt64(0), CheckRunKind.Full, reader.GetFieldValue<DateTimeOffset>(1), reader.GetFieldValue<DateTimeOffset>(2), ParseVerdict(reader.GetString(3)))
            : null;
    }

    public async Task AppendConfirmationAsync(long runId, CheckConfirmation confirmation, CancellationToken cancellationToken)
    {
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await InsertConfirmationAsync(connection, null, runId, confirmation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CheckConfirmation?> LatestConfirmationAsync(long intentId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT c.actor, c.reason, c.codes, c.at FROM execution.check_confirmations c JOIN execution.check_runs r ON r.id = c.run_id WHERE r.intent_id = @intent ORDER BY c.run_id DESC LIMIT 1");
        command.Parameters.AddWithValue("intent", intentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new CheckConfirmation(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<string[]>(2), reader.GetFieldValue<DateTimeOffset>(3))
            : null;
    }

    private static async Task InsertConfirmationAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, long runId, CheckConfirmation c, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("INSERT INTO execution.check_confirmations (run_id, actor, reason, codes, at) VALUES (@run, @actor, @reason, @codes, @at)", connection, tx);
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("actor", c.Actor);
        command.Parameters.AddWithValue("reason", c.Reason);
        command.Parameters.Add(new NpgsqlParameter("codes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = c.Codes.ToArray() });
        command.Parameters.AddWithValue("at", c.At);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object Finite(double? v) => v is { } x && double.IsFinite(x) ? x : DBNull.Value;

    private static string VerdictText(CheckVerdict v) => v switch
    {
        CheckVerdict.Cleared => "cleared",
        CheckVerdict.ClearedByConfirmation => "cleared_by_confirmation",
        _ => "rejected",
    };

    // An unknown stored text reads as rejected: never a pass.
    private static CheckVerdict ParseVerdict(string text) => text switch
    {
        "cleared" => CheckVerdict.Cleared,
        "cleared_by_confirmation" => CheckVerdict.ClearedByConfirmation,
        _ => CheckVerdict.Rejected,
    };
}
