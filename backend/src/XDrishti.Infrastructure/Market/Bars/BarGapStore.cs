using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// The gap records (<c>market.bar_gaps</c>, ADR 0017 §3). The runtime role may insert and update them, never delete. The summary and the list count only the gaps of tracked, not retired instruments
/// (like the quality universe): the gaps of other instruments stay stored but are not shown, and come back when the instrument is tracked again.
/// </summary>
internal sealed class BarGapStore(XDrishtiDbContext db, NpgsqlDataSource dataSource) : IBarGaps
{
    private const string Upsert =
        """
        INSERT INTO market.bar_gaps (instrument_id, date, expected, found, attempts, last_attempt_at, status, detected_at)
        VALUES (@id, @date, @expected, @found, @attempts, @lastAttempt, @status, @detectedAt)
        ON CONFLICT (instrument_id, date) DO UPDATE SET
            expected = EXCLUDED.expected, found = EXCLUDED.found, attempts = EXCLUDED.attempts,
            last_attempt_at = EXCLUDED.last_attempt_at, status = EXCLUDED.status, detected_at = EXCLUDED.detected_at
        """;

    public async ValueTask<IReadOnlyList<BarGap>> ListForAsync(Guid instrumentId, DateOnly since, CancellationToken cancellationToken) =>
        await db.BarGaps.AsNoTracking()
            .Where(g => g.InstrumentId == instrumentId && (g.Status != GapStatus.Filled || g.Date >= since))
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

    public async ValueTask SaveAsync(IReadOnlyCollection<BarGap> gaps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        if (gaps.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var gap in gaps)
        {
            await using var command = new NpgsqlCommand(Upsert, connection, transaction);
            command.Parameters.AddWithValue("id", gap.InstrumentId);
            command.Parameters.AddWithValue("date", gap.Date);
            command.Parameters.AddWithValue("expected", gap.Expected);
            command.Parameters.AddWithValue("found", gap.Found);
            command.Parameters.AddWithValue("attempts", gap.Attempts);
            command.Parameters.AddWithValue("lastAttempt", NpgsqlDbType.TimestampTz, (object?)gap.LastAttemptAt ?? DBNull.Value);
            command.Parameters.AddWithValue("status", gap.Status.ToCode());
            command.Parameters.AddWithValue("detectedAt", gap.DetectedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<GapSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT count(*) FILTER (WHERE g.status = 'open'), count(*) FILTER (WHERE g.status = 'unfillable'), count(*) FILTER (WHERE g.status = 'filled'), max(g.detected_at)
            FROM market.bar_gaps g
            JOIN market.instruments i ON i.id = g.instrument_id AND i.retired_at IS NULL
            JOIN market.tracked_instruments t ON t.instrument_id = i.id AND t.active
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new GapSummary((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async ValueTask<GapPage> ListAsync(GapSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        const string Where =
            """
            FROM market.bar_gaps g
            JOIN market.instruments i ON i.id = g.instrument_id AND i.retired_at IS NULL
            JOIN market.tracked_instruments t ON t.instrument_id = i.id AND t.active
            WHERE (@status IS NULL OR g.status = @status) AND (@instrument IS NULL OR g.instrument_id = @instrument)
            """;

        void Bind(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("status", NpgsqlDbType.Text, search.Status is { } s ? s.ToCode() : DBNull.Value);
            command.Parameters.AddWithValue("instrument", NpgsqlDbType.Uuid, search.InstrumentId is { } id ? id : DBNull.Value);
        }

        int total;
        await using (var count = dataSource.CreateCommand($"SELECT count(*) {Where}"))
        {
            Bind(count);
            total = (int)(long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = dataSource.CreateCommand(
            $"""
            SELECT g.instrument_id, i.canonical_symbol, i.name, g.date, g.expected, g.found, g.attempts, g.status, g.last_attempt_at, g.detected_at
            {Where}
            ORDER BY g.date DESC, i.canonical_symbol
            OFFSET @offset LIMIT @limit
            """);
        Bind(command);
        command.Parameters.AddWithValue("offset", (search.Page - 1) * search.PageSize);
        command.Parameters.AddWithValue("limit", search.PageSize);
        var items = new List<GapRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new GapRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return new GapPage(items, search.Page, search.PageSize, total);
    }
}
