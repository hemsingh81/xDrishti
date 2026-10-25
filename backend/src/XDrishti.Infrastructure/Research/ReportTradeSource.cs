using System.Runtime.CompilerServices;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The read side the reports use over the result store (P8-06, ADR 0100): the run headers with the labels a report may take from storage, and the closed trades of chosen runs as a streamed projection of the few columns a report needs, read with a plain reader and never
/// through the change tracker (a 500,000-trade run must not become 500,000 tracked entities). Read only: nothing here writes. The filters of a query are parameters of the statement, never text in it. Never logs a trade, a symbol or a strategy.
/// </summary>
internal sealed class ReportTradeSource(NpgsqlDataSource dataSource) : IReportTradeSource
{
    private const string RunsSql = """
        SELECT r.run_key, r.strategy_id, r.universe_kind, r.universe_label, r.instruments, r.instruments_run, r.from_date, r.to_date, r.costs_sheet_id,
               r.trade_count, r.open_at_end_count, r.created_at, s.style,
               (SELECT t.basket_ids FROM research.sim_trades t WHERE t.run_id = r.id ORDER BY t.open_at_end, t.ordinal LIMIT 1) AS first_baskets, r.costs_rates_sha256
        FROM research.sim_runs r
        LEFT JOIN lab.strategies s ON s.id = r.strategy_id
        ORDER BY r.created_at DESC, r.id DESC
        """;

    private const string ComparisonMembersSql = """
        SELECT m.run_key, m.candidate_id, c.comparison_key
        FROM research.sim_comparisons c
        CROSS JOIN LATERAL unnest(c.member_run_keys, c.member_candidate_ids) AS m(run_key, candidate_id)
        ORDER BY c.comparison_key, m.run_key
        """;

    private const string RunIdsSql = "SELECT id, run_key FROM research.sim_runs WHERE run_key = ANY(@keys)";

    private const string TradesSelect = """
        SELECT t.run_id, t.trade_key, t.strategy_id, t.trigger_timeframe, t.side, t.instrument_id, t.symbol, t.basket_ids, t.trade_date, t.exit_epoch_seconds, t.r_filled, t.r_net, t.sessions_held
        FROM research.sim_trades t
        WHERE t.run_id = ANY(@run_ids) AND t.open_at_end = false
        """;

    private static readonly ValueList<string> NoBaskets = new([]);

    public async ValueTask<IReadOnlyList<ReportRunInfo>> ListRunsAsync(CancellationToken cancellationToken)
    {
        var exits = await ExitsAsync(cancellationToken);
        var runs = new List<ReportRunInfo>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(RunsSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var runKey = reader.GetString(0).Trim();
            var kind = reader.GetString(2);
            var label = reader.GetString(3);
            var firstBaskets = reader.IsDBNull(13) ? [] : reader.GetFieldValue<Guid[]>(13);
            var universe = kind == EnumText<UniverseKind>.Name(UniverseKind.Basket) && firstBaskets.Length > 0
                ? "basket:" + firstBaskets[0].ToString("D")
                : $"{(kind == EnumText<UniverseKind>.Name(UniverseKind.Basket) ? "basket" : "list")}:{label}:{reader.GetInt32(4)}:{reader.GetInt32(5)}";
            var style = reader.IsDBNull(12) ? null : reader.GetString(12);
            exits.TryGetValue(runKey, out var exit);
            runs.Add(new ReportRunInfo(
                runKey,
                reader.GetString(1),
                kind,
                label,
                universe,
                reader.GetFieldValue<DateOnly>(6),
                reader.GetFieldValue<DateOnly>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                style,
                style is null ? null : "stored strategy",
                exit.Exit,
                exit.Source,
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(14) ? null : reader.GetString(14).Trim()));
        }

        return runs.AsReadOnly();
    }

    public async IAsyncEnumerable<ReportTrade> StreamTradesAsync(IReadOnlyList<string> runKeys, ReportTradeFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKeys);
        ArgumentNullException.ThrowIfNull(filter);
        if (runKeys.Count == 0)
        {
            yield break;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var runs = new Dictionary<long, string>();
        await using (var ids = new NpgsqlCommand(RunIdsSql, connection))
        {
            ids.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Char, runKeys.ToArray());
            await using var idReader = await ids.ExecuteReaderAsync(cancellationToken);
            while (await idReader.ReadAsync(cancellationToken))
            {
                runs[idReader.GetInt64(0)] = idReader.GetString(1).Trim();
            }
        }

        if (runs.Count == 0)
        {
            yield break;
        }

        await using var command = new NpgsqlCommand { Connection = connection };
        var sql = new StringBuilder(TradesSelect);
        command.Parameters.AddWithValue("run_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, runs.Keys.ToArray());
        AnyOf(sql, command, "strategy_id", filter.Strategies);
        AnyOf(sql, command, "trigger_timeframe", [.. filter.Timeframes.Select(CodeToName)]);
        AnyOf(sql, command, "side", [.. filter.Sides.Select(s => s.ToLowerInvariant())]);
        AnyOf(sql, command, "symbol", filter.Symbols);
        if (filter.Baskets.Count > 0)
        {
            sql.Append(" AND t.basket_ids && @baskets");
            command.Parameters.AddWithValue("baskets", NpgsqlDbType.Array | NpgsqlDbType.Uuid, filter.Baskets.ToArray());
        }

        if (filter.From is { } from)
        {
            sql.Append(" AND t.trade_date >= @date_from");
            command.Parameters.AddWithValue("date_from", NpgsqlDbType.Date, from);
        }

        if (filter.To is { } to)
        {
            sql.Append(" AND t.trade_date <= @date_to");
            command.Parameters.AddWithValue("date_to", NpgsqlDbType.Date, to);
        }

        sql.Append(" ORDER BY t.run_id, t.ordinal");
        command.CommandText = sql.ToString();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var timeframes = new Dictionary<string, string>(StringComparer.Ordinal);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        var singleBasket = new Dictionary<Guid, ValueList<string>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return Read(reader, runs, timeframes, strings, singleBasket);
        }
    }

    private static ReportTrade Read(NpgsqlDataReader reader, Dictionary<long, string> runs, Dictionary<string, string> timeframes, Dictionary<string, string> strings, Dictionary<Guid, ValueList<string>> singleBasket)
    {
        var timeframeName = reader.GetString(3);
        if (!timeframes.TryGetValue(timeframeName, out var timeframe))
        {
            timeframe = EnumText<Timeframe>.Parse(timeframeName).ToCode();
            timeframes[timeframeName] = timeframe;
        }

        var side = reader.GetString(4) switch
        {
            "buy" => "BUY",
            "sell" => "SELL",
            _ => throw new StoreReadException("A stored trade has a side that is neither buy nor sell."),
        };
        var baskets = reader.GetFieldValue<Guid[]>(7);
        ValueList<string> basketIds;
        if (baskets.Length == 0)
        {
            basketIds = NoBaskets;
        }
        else if (baskets.Length == 1)
        {
            if (!singleBasket.TryGetValue(baskets[0], out basketIds!))
            {
                basketIds = new ValueList<string>([baskets[0].ToString("D")]);
                singleBasket[baskets[0]] = basketIds;
            }
        }
        else
        {
            basketIds = new ValueList<string>(baskets.Select(b => b.ToString("D")));
        }

        return new ReportTrade(
            runs[reader.GetInt64(0)],
            reader.GetString(1),
            Intern(strings, reader.GetString(2)),
            timeframe,
            side,
            reader.GetGuid(5),
            Intern(strings, reader.GetString(6)),
            basketIds,
            reader.GetFieldValue<DateOnly>(8),
            SessionClock.DateOf(reader.GetInt64(9)),
            reader.GetDouble(10),
            reader.IsDBNull(11) ? null : reader.GetDouble(11),
            reader.GetInt32(12));
    }

    private static string Intern(Dictionary<string, string> strings, string value)
    {
        if (strings.TryGetValue(value, out var known))
        {
            return known;
        }

        strings[value] = value;
        return value;
    }

    private static string CodeToName(string code) =>
        Timeframes.TryParse(code, out var timeframe) ? EnumText<Timeframe>.Name(timeframe) : throw new ArgumentException("A timeframe filter is a code the query parser accepted.", nameof(code));

    private static void AnyOf(StringBuilder sql, NpgsqlCommand command, string column, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        sql.Append(" AND t.").Append(column).Append(" = ANY(@").Append(column).Append(')');
        command.Parameters.AddWithValue(column, NpgsqlDbType.Array | NpgsqlDbType.Text, values.ToArray());
    }

    /// <summary>The exit candidate of each run that is a member of a stored comparison; null when a run is in comparisons that name different candidates (then the exit is not recorded rather than guessed).</summary>
    private async Task<Dictionary<string, (string? Exit, string? Source)>> ExitsAsync(CancellationToken cancellationToken)
    {
        var seen = new Dictionary<string, (string Candidate, string Comparison, bool Ambiguous)>(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ComparisonMembersSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var runKey = reader.GetString(0).Trim();
            var candidate = reader.GetString(1);
            var comparison = reader.GetString(2).Trim();
            if (!seen.TryGetValue(runKey, out var first))
            {
                seen[runKey] = (candidate, comparison, false);
            }
            else if (!string.Equals(first.Candidate, candidate, StringComparison.Ordinal))
            {
                seen[runKey] = first with { Ambiguous = true };
            }
        }

        return seen.ToDictionary(
            e => e.Key,
            e => e.Value.Ambiguous ? ((string?)null, (string?)null) : (e.Value.Candidate, "stored comparison " + e.Value.Comparison[..Math.Min(12, e.Value.Comparison.Length)]),
            StringComparer.Ordinal);
    }
}
