using Npgsql;
using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Infrastructure.OptionBackfill;

/// <summary>The resume point of the backfill (P14-03): what <c>options.fetch_log</c> says about each key, and how many requests were sent since a time. Reads the log only, never a bar.</summary>
internal sealed class BackfillFetchLog(NpgsqlDataSource dataSource) : IBackfillFetchLog
{
    private const string State =
        """
        SELECT underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from,
               count(*)::int AS attempts,
               (count(*) FILTER (WHERE outcome IN ('timeout', 'server_error', 'bad_body')))::int AS failed,
               max(outcome) FILTER (WHERE outcome IN ('ok', 'empty', 'no_data')) AS done,
               (count(*) FILTER (WHERE outcome = 'store_failed'))::int AS store_failed
        FROM options.fetch_log
        GROUP BY underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from
        """;

    public async ValueTask<IReadOnlyDictionary<BackfillKey, KeyState>> LoadStateAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(State);
        var states = new Dictionary<BackfillKey, KeyState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = new BackfillKey(reader.GetGuid(0), reader.GetString(1)[0], reader.GetInt16(2), reader.GetInt16(3), reader.GetString(4), reader.GetInt16(5), DateOnly.FromDateTime(reader.GetDateTime(6)));
            states[key] = new KeyState(reader.GetInt32(7), reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt32(10));
        }

        return states;
    }

    public async ValueTask<int> CountSentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(*)::int FROM options.fetch_log WHERE started_at >= @since AND outcome <> 'disk_stop'");
        command.Parameters.AddWithValue("since", since.UtcDateTime);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
