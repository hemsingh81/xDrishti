using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Execution;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>Reads the readiness record as the runtime role (SELECT only): for each account that is at assisted or has any record, the session's newest check, the unbroken run of ready checks, the attestation, the last 20 day states and the highest sends per second.</summary>
internal sealed class ExecutionReadinessReader(XDrishtiDbContext db) : IExecutionReadinessReader
{
    public Task<IReadOnlyList<ExecutionReadinessAccount>> ReadAsync(DateOnly date, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ExecutionReadinessAccount>>(db, async connection =>
        {
            List<(Guid Id, string Name)> accounts = [];
            await using (var list = new NpgsqlCommand(
                """
                SELECT a.id, a.name FROM accounts.broker_accounts a
                WHERE execution.effective_mode(a.id) = 'assisted' OR EXISTS (SELECT 1 FROM execution.readiness_checks c WHERE c.account_id = a.id)
                   OR EXISTS (SELECT 1 FROM execution.day_states d WHERE d.account_id = a.id) OR EXISTS (SELECT 1 FROM execution.ip_attestations t WHERE t.account_id = a.id)
                ORDER BY a.name, a.id
                """, connection))
            await using (var reader = await list.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    accounts.Add((reader.GetGuid(0), reader.GetString(1)));
                }
            }

            List<ExecutionReadinessAccount> views = [];
            foreach (var (id, name) in accounts)
            {
                views.Add(await ViewAsync(connection, id, name, date, cancellationToken));
            }

            return views;
        }, cancellationToken);

    private static async Task<ExecutionReadinessAccount> ViewAsync(NpgsqlConnection connection, Guid id, string name, DateOnly date, CancellationToken cancellationToken)
    {
        string state = "unavailable";
        IReadOnlyList<ExecutionReadinessCheck> checks = [];
        DateTimeOffset? checkedAt = null;
        DateTimeOffset? tokenEnd = null;
        DateTimeOffset? readyAt = null;
        var newest = true;
        var inRun = true;
        await using (var command = new NpgsqlCommand("SELECT at, verdict, reasons::text, token_expires_at FROM execution.readiness_checks WHERE account_id = @a AND session_date = @d ORDER BY id DESC LIMIT 500", connection))
        {
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = id });
            command.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = date });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var verdict = reader.GetString(1);
                if (newest)
                {
                    state = verdict;
                    checkedAt = reader.GetFieldValue<DateTimeOffset>(0);
                    checks = Checks(reader.GetString(2));
                    tokenEnd = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3);
                    newest = false;
                }

                if (inRun && state == "ready" && verdict == "ready")
                {
                    readyAt = reader.GetFieldValue<DateTimeOffset>(0);
                }
                else
                {
                    inRun = false;
                }
            }
        }

        DateOnly? modifyAfter = null;
        await using (var command = new NpgsqlCommand("SELECT ip_modify_after FROM execution.readiness_checks WHERE account_id = @a AND ip_modify_after IS NOT NULL ORDER BY id DESC LIMIT 1", connection))
        {
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = id });
            if (await command.ExecuteScalarAsync(cancellationToken) is DateOnly d)
            {
                modifyAfter = d;
            }
        }

        ExecutionIpAttestationView? attestation = null;
        await using (var command = new NpgsqlCommand("SELECT id, ip, orders_allowed, note, at FROM execution.ip_attestations WHERE account_id = @a ORDER BY id DESC LIMIT 1", connection))
        {
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = id });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                attestation = new(reader.GetInt64(0), id, reader.GetString(1), reader.GetBoolean(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4));
            }
        }

        List<ExecutionDayStateView> days = [];
        await using (var command = new NpgsqlCommand("SELECT session_date, state, late, at, reasons::text FROM execution.day_states WHERE account_id = @a ORDER BY session_date DESC LIMIT 20", connection))
        {
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = id });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                days.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetBoolean(2), reader.GetFieldValue<DateTimeOffset>(3), [.. Checks(reader.GetString(4)).Where(c => c.Outcome != "pass").Select(c => c.Code)]));
            }
        }

        var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        int? sends = null;
        await using (var command = new NpgsqlCommand(
            "SELECT max(c) FROM (SELECT count(*) AS c FROM execution.broker_calls WHERE account_id = @a AND call = 'orders_post' AND phase = 'begin' AND at >= @from AND at < @to GROUP BY date_trunc('second', at)) s", connection))
        {
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = id });
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.ToUniversalTime() });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = from.AddDays(1).ToUniversalTime() });
            if (await command.ExecuteScalarAsync(cancellationToken) is long peak)
            {
                sends = (int)peak;
            }
        }

        return new(id, name, state, checks, checkedAt, tokenEnd, readyAt, modifyAfter, attestation, null, days, sends);
    }

    private static List<ExecutionReadinessCheck> Checks(string json)
    {
        List<ExecutionReadinessCheck> list = [];
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string Str(string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
            list.Add(new(Str("code"), Str("outcome"), Str("text"), e.TryGetProperty("ageSeconds", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : null));
        }

        return list;
    }
}

/// <summary>Writes the owner's attestation (the only INSERT the runtime role holds on the readiness tables). The row is stamped with the database time by a trigger.</summary>
internal sealed class ExecutionIpAttestations(XDrishtiDbContext db) : IExecutionIpAttestations
{
    public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM accounts.broker_accounts WHERE id = @a)", connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
            return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }, cancellationToken);

    public Task<ExecutionIpAttestationView> InsertAsync(Guid accountId, string ip, bool ordersAllowed, string note, string actor, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            await using var command = new NpgsqlCommand("INSERT INTO execution.ip_attestations (account_id, ip, orders_allowed, note, actor) VALUES (@a, @ip, @allowed, @note, @actor) RETURNING id, at", connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
            command.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Varchar) { Value = ip });
            command.Parameters.Add(new NpgsqlParameter("allowed", NpgsqlDbType.Boolean) { Value = ordersAllowed });
            command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Varchar) { Value = note });
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Varchar) { Value = actor });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new ExecutionIpAttestationView(reader.GetInt64(0), accountId, ip, ordersAllowed, note, reader.GetFieldValue<DateTimeOffset>(1));
        }, cancellationToken);
}

/// <summary>P11-05's newest sebi_recheck, from the same store the compliance routes read.</summary>
internal sealed class ComplianceRecords(IComplianceStore store) : IComplianceRecords
{
    public async ValueTask<ExecutionRecheckView?> LatestSebiRecheckAsync(CancellationToken cancellationToken)
    {
        var newest = (await store.LatestPerKindAsync(cancellationToken)).FirstOrDefault(s => s.Record.Kind == ComplianceKinds.SebiRecheck);
        return newest is null ? null : new(newest.Record.Outcome, newest.Record.DecidedOn, newest.Record.NextReviewOn);
    }
}
