using Npgsql;
using XDrishti.Execution.Application.Halts;

namespace XDrishti.Execution.Persistence;

/// <summary>The day marks, the drills and the kill-switch log as the role xd_exec: INSERT and SELECT only (the tables are insert-only).</summary>
internal sealed class PgDayMarkStore(NpgsqlDataSource data) : IDayMarkStore
{
    public async Task<bool> AppendAsync(DayMark mark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mark);
        await using var command = data.CreateCommand(
            "INSERT INTO execution.day_marks (account_id, session_date, realised, marked, capital, peak, at) VALUES (@a, @d, @r, @m, @c, @p, @at) ON CONFLICT (account_id, session_date) DO NOTHING");
        command.Parameters.AddWithValue("a", mark.AccountId);
        command.Parameters.AddWithValue("d", mark.SessionDate);
        command.Parameters.AddWithValue("r", mark.Realised);
        command.Parameters.AddWithValue("m", mark.Marked);
        command.Parameters.AddWithValue("c", mark.Capital);
        command.Parameters.AddWithValue("p", mark.Peak);
        command.Parameters.AddWithValue("at", mark.At);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<DayMark>> LatestAsync(Guid accountId, int count, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT account_id, session_date, realised, marked, capital, peak, at FROM execution.day_marks WHERE account_id = @a ORDER BY session_date DESC LIMIT @n");
        command.Parameters.AddWithValue("a", accountId);
        command.Parameters.AddWithValue("n", count);
        var rows = new List<DayMark>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DayMark(reader.GetGuid(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return rows;
    }
}

internal sealed class PgDrillStore(NpgsqlDataSource data) : IDrillStore
{
    public async Task AppendAsync(DrillResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var command = data.CreateCommand("INSERT INTO execution.drills (at, account_id, passed, latency_ms, detail) VALUES (@at, @a, @p, @l, @d)");
        command.Parameters.AddWithValue("at", result.At);
        command.Parameters.AddWithValue("a", result.AccountId);
        command.Parameters.AddWithValue("p", result.Passed);
        command.Parameters.AddWithValue("l", result.LatencyMs);
        command.Parameters.AddWithValue("d", result.Detail.Length > 300 ? result.Detail[..300] : result.Detail);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class PgKillSwitchLog(NpgsqlDataSource data) : IKillSwitchLog
{
    public async Task AppendAsync(long haltId, string outcome, string detail, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("INSERT INTO execution.kill_switch_log (at, halt_id, outcome, detail) VALUES (@at, @h, @o, @d)");
        command.Parameters.AddWithValue("at", at);
        command.Parameters.AddWithValue("h", haltId);
        command.Parameters.AddWithValue("o", outcome);
        command.Parameters.AddWithValue("d", detail.Length > 100 ? detail[..100] : detail);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> OutcomesAsync(long haltId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT outcome FROM execution.kill_switch_log WHERE halt_id = @h ORDER BY id");
        command.Parameters.AddWithValue("h", haltId);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
