using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionReports;

namespace XDrishti.Infrastructure.OptionReports;

/// <summary>
/// The select-only reads of the P14 reports (spec P14-16, ADR 0201): aggregates of the archive's quality results, the stored expression runs with counts over their paired rows, and the stored pair formations. Every query is bounded by a date range, a run id or
/// profile, or a limit, and returns counts, shares' inputs, dates and words: no price, IV, Greek, bar, quote or return figure is selected. The UTC instant is given to Npgsql; dates are India dates.
/// </summary>
internal sealed class OptionReportReader(NpgsqlDataSource dataSource) : IOptionReportReader
{
    private const string RunColumns = "run_id, cash_run_key, profile_hash, remark_version, created_at, trades, paired, unpaired::text, bs_fallback_share, labels";

    public async ValueTask<IReadOnlyList<Guid>> ArchiveUnderlyingsAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT DISTINCT underlying_id FROM options.quality_results WHERE source = 'chain' AND session_date >= @from AND session_date <= @to ORDER BY underlying_id LIMIT @limit");
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    public async ValueTask<IReadOnlyList<ExpressionRunStored>> ExpressionRunsAsync(string? profileHash, int limit, CancellationToken cancellationToken)
    {
        var filter = profileHash is null ? string.Empty : "WHERE profile_hash = @profile ";
        await using var command = dataSource.CreateCommand($"SELECT {RunColumns} FROM research.expression_runs {filter}ORDER BY created_at DESC, run_id DESC LIMIT @limit");
        if (profileHash is not null)
        {
            command.Parameters.AddWithValue("profile", NpgsqlDbType.Char, profileHash);
        }

        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var heads = new List<ExpressionRunStored>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                heads.Add(Head(reader));
            }
        }

        return await WithCountsAsync(heads, cancellationToken);
    }

    public async ValueTask<ExpressionRunStored?> ExpressionRunAsync(string runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {RunColumns} FROM research.expression_runs WHERE run_id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Char, runId ?? string.Empty);
        ExpressionRunStored? head = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                head = Head(reader);
            }
        }

        return head is null ? null : (await WithCountsAsync([head], cancellationToken))[0];
    }

    public async ValueTask<IReadOnlyList<PairFormationStored>> PairFormationsAsync(IReadOnlyList<string> families, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(families);
        await using var command = dataSource.CreateCommand(
            """
            SELECT formation_id, family, quarter, window_from, window_to, created_at, candidates, tested, unavailable, selected, detail::text
            FROM research.pair_formations
            WHERE family = ANY(@families) AND window_to >= @from AND window_from <= @to
            ORDER BY quarter, created_at, formation_id
            LIMIT @limit
            """);
        command.Parameters.AddWithValue("families", NpgsqlDbType.Array | NpgsqlDbType.Varchar, families.ToArray());
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var list = new List<PairFormationStored>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PairFormationStored(reader.GetString(0).Trim(), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetString(10)));
        }

        return list;
    }

    private static ExpressionRunStored Head(NpgsqlDataReader reader) => new(
        reader.GetString(0).Trim(), reader.GetString(1).Trim(), reader.GetString(2).Trim(), reader.GetString(3).Trim(), reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetDouble(8), reader.GetFieldValue<string[]>(9), 0, 0, new Dictionary<string, int>());

    /// <summary>The counts over the paired rows of the runs, one grouped read by run id (a prefix of the primary key).</summary>
    private async ValueTask<IReadOnlyList<ExpressionRunStored>> WithCountsAsync(List<ExpressionRunStored> heads, CancellationToken cancellationToken)
    {
        if (heads.Count == 0)
        {
            return heads;
        }

        await using var command = dataSource.CreateCommand(
            """
            SELECT run_id, sizable, count(*)::int,
                   (count(*) FILTER (WHERE expiry_source = 'derived'))::int,
                   (count(*) FILTER (WHERE 'spread_from_recent_archive' = ANY(labels)))::int
            FROM research.expression_trades
            WHERE run_id = ANY(@ids) AND fate = 'paired'
            GROUP BY run_id, sizable
            """);
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Char, heads.Select(h => h.RunId).ToArray());
        var counts = new Dictionary<string, (int Derived, int Spread, Dictionary<string, int> Sizable)>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0).Trim();
                var entry = counts.TryGetValue(id, out var seen) ? seen : (Derived: 0, Spread: 0, Sizable: new Dictionary<string, int>(StringComparer.Ordinal));
                entry.Sizable[reader.GetString(1)] = reader.GetInt32(2);
                counts[id] = (entry.Derived + reader.GetInt32(3), entry.Spread + reader.GetInt32(4), entry.Sizable);
            }
        }

        return [.. heads.Select(h => counts.TryGetValue(h.RunId, out var c) ? h with { DerivedExpiry = c.Derived, SpreadFromRecentArchive = c.Spread, Sizable = c.Sizable } : h)];
    }
}
