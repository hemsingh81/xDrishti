using Npgsql;
using XDrishti.Application.Abstractions.Options;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform;

namespace XDrishti.Infrastructure.OptionContracts;

/// <summary>
/// The setting <c>options.underlyings</c> as a row of <c>platform.app_settings</c> (ADR 0002), and the turnover ranking its default is built from once (the
/// mean close x volume of the last 20 daily bars, as the depth set ranks shares, P7-16) among tracked, F&amp;O-eligible, not retired shares.
/// </summary>
internal sealed class OptionUnderlyingSetting(NpgsqlDataSource dataSource) : IOptionUnderlyingSetting
{
    public const string Key = AppSettingKeys.OptionUnderlyings;

    private const int Sessions = 20;

    public async ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT value FROM platform.app_settings WHERE key = @key");
        command.Parameters.AddWithValue("key", Key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async ValueTask<RankedShares> MostTradedFnoSharesAsync(DateOnly before, int take, CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            return new RankedShares([], null);
        }

        var until = new DateTimeOffset(before.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        await using var command = dataSource.CreateCommand(
            """
            WITH recent AS (
                SELECT a.instrument_id, a.ts, a.close * a.volume AS turnover,
                       row_number() OVER (PARTITION BY a.instrument_id ORDER BY a.ts DESC) AS n
                FROM market.bars_agg a
                JOIN market.tracked_instruments t ON t.instrument_id = a.instrument_id AND t.active
                WHERE a.timeframe = '1D' AND a.ts < @until AND a.ts >= @since
            )
            SELECT i.canonical_symbol, max(r.ts) FILTER (WHERE r.n = 1) AS newest
            FROM recent r
            JOIN market.instruments i ON i.id = r.instrument_id AND i.retired_at IS NULL AND i.type = 'share' AND i.fno_eligible
            WHERE r.n <= @sessions
            GROUP BY i.canonical_symbol
            HAVING count(*) = @sessions
            ORDER BY avg(r.turnover) DESC, i.canonical_symbol
            LIMIT @take
            """);
        command.Parameters.AddWithValue("until", until.UtcDateTime);
        command.Parameters.AddWithValue("since", until.AddDays(-60).UtcDateTime);
        command.Parameters.AddWithValue("sessions", (long)Sessions);
        command.Parameters.AddWithValue("take", take);
        var symbols = new List<string>();
        DateOnly? newest = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            symbols.Add(reader.GetString(0));
            var day = IndiaTime.Today(new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero));
            newest = newest is null || day > newest ? day : newest;
        }

        return new RankedShares(symbols, newest);
    }

    public async ValueTask<bool> FixDefaultAsync(string value, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES (@key, @value, @actor, @now) ON CONFLICT (key) DO NOTHING");
        command.Parameters.AddWithValue("key", Key);
        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
