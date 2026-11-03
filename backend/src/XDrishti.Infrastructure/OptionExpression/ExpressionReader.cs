using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Evaluation;

namespace XDrishti.Infrastructure.OptionExpression;

/// <summary>The select-only reads of protocol O over a stored expression run (P14-09, ADR 0198). The windows are in the query: every read that returns figures filters by side and by the entry instant (UTC bounds from the India dates) and by the run id, so a hold-out trade is never fetched by a caller that did not ask for its dates.</summary>
internal sealed class ExpressionReader(NpgsqlDataSource dataSource) : IExpressionReader
{
    private const string IndiaDate = "(entry_ts AT TIME ZONE 'Asia/Kolkata')::date";

    public async ValueTask<ExpressionRunRef?> FindRunAsync(string cashRunKey, string profileHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT run_id, created_at FROM research.expression_runs WHERE cash_run_key = @cash AND profile_hash = @profile ORDER BY created_at DESC, run_id DESC LIMIT 1");
        command.Parameters.AddWithValue("cash", NpgsqlDbType.Char, cashRunKey ?? string.Empty);
        command.Parameters.AddWithValue("profile", NpgsqlDbType.Char, profileHash ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new ExpressionRunRef(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)) : null;
    }

    public async ValueTask<ExpressionDates> DatesAsync(string runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT side, min({IndiaDate}) FILTER (WHERE fate <> 'open_at_end'), max({IndiaDate}) FILTER (WHERE fate <> 'open_at_end'), max({IndiaDate}) FILTER (WHERE fate = 'paired') FROM research.expression_trades WHERE run_id = @id GROUP BY side ORDER BY side");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Char, runId ?? string.Empty);
        var sides = new List<SideBounds>();
        DateOnly? lastPaired = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(1) && !reader.IsDBNull(2))
            {
                sides.Add(new SideBounds(reader.GetString(0).Trim(), reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<DateOnly>(2)));
            }

            if (!reader.IsDBNull(3))
            {
                var date = reader.GetFieldValue<DateOnly>(3);
                lastPaired = lastPaired is { } seen && seen > date ? seen : date;
            }
        }

        return new ExpressionDates(sides, lastPaired);
    }

    public async ValueTask<IReadOnlyList<ExpressionPairRow>> PairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken)
    {
        var rows = new List<ExpressionPairRow>();
        if (windows.Count == 0)
        {
            return rows;
        }

        await using var command = dataSource.CreateCommand("SELECT trade_key, entry_ts, exit_ts, r_equiv, cash_r, net_inr IS NOT NULL, expiry_source, sizable, labels FROM research.expression_trades WHERE run_id = @id AND fate = 'paired' AND (" + Predicate(windows) + ") ORDER BY entry_ts, trade_key");
        Bind(command, runId, windows);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ExpressionPairRow(
                reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetBoolean(5), reader.GetString(6).Trim(), reader.GetString(7), reader.GetFieldValue<string[]>(8)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyDictionary<string, int>> UnpairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (windows.Count == 0)
        {
            return counts;
        }

        await using var command = dataSource.CreateCommand("SELECT fate, count(*) FROM research.expression_trades WHERE run_id = @id AND fate NOT IN ('paired', 'open_at_end') AND (" + Predicate(windows) + ") GROUP BY fate");
        Bind(command, runId, windows);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = (int)reader.GetInt64(1);
        }

        return counts;
    }

    private static string Predicate(IReadOnlyList<ExpressionWindow> windows)
    {
        if (windows.Count > IExpressionReader.MaxWindows)
        {
            throw new ArgumentOutOfRangeException(nameof(windows), "Too many windows for one read.");
        }

        var text = new StringBuilder();
        for (var i = 0; i < windows.Count; i++)
        {
            text.Append(i == 0 ? string.Empty : " OR ").Append(CultureInfo.InvariantCulture, $"(side = @s{i} AND entry_ts >= @a{i} AND entry_ts < @b{i})");
        }

        return text.ToString();
    }

    private static void Bind(NpgsqlCommand command, string runId, IReadOnlyList<ExpressionWindow> windows)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Char, runId ?? string.Empty);
        for (var i = 0; i < windows.Count; i++)
        {
            command.Parameters.AddWithValue("s" + i.ToString(CultureInfo.InvariantCulture), NpgsqlDbType.Text, windows[i].Side);
            command.Parameters.AddWithValue("a" + i.ToString(CultureInfo.InvariantCulture), NpgsqlDbType.TimestampTz, Midnight(windows[i].Start));
            command.Parameters.AddWithValue("b" + i.ToString(CultureInfo.InvariantCulture), NpgsqlDbType.TimestampTz, Midnight(windows[i].End.AddDays(1)));
        }
    }

    /// <summary>The start of the India day as a UTC instant: Npgsql is given UTC only.</summary>
    private static DateTimeOffset Midnight(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();
}
