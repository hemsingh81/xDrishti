using System.Globalization;
using System.Text.Json;
using Npgsql;
using XDrishti.Application.Abstractions.Reporting;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The read side of the reports over the stored comparisons and exit choices (spec P8-10 part b, ADR 0114): comparison headers (never the CSV text) and the record of each exit choice, read with a plain reader. Read only. Never logs a record.
/// </summary>
internal sealed class ReportComparisonSource(NpgsqlDataSource dataSource) : IReportComparisonSource
{
    private const string ComparisonsSql = """
        SELECT comparison_key, entries_key, strategy_id, unit, common_entries, member_run_keys, member_candidate_ids, created_at
        FROM research.sim_comparisons
        ORDER BY created_at DESC, id DESC
        """;

    private const string SelectionsSql = """
        SELECT s.selection_key, c.comparison_key, s.strategy_id, s.timeframe, s.side, s.style, s.regime, s.record_json, s.created_at
        FROM research.exit_selections s
        JOIN research.sim_comparisons c ON c.id = s.comparison_id
        ORDER BY s.created_at DESC, s.id DESC
        """;

    public async ValueTask<IReadOnlyList<ReportComparisonInfo>> ListComparisonsAsync(CancellationToken cancellationToken)
    {
        var list = new List<ReportComparisonInfo>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ComparisonsSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var keys = reader.GetFieldValue<string[]>(5);
            var candidates = reader.GetFieldValue<string[]>(6);
            list.Add(new ReportComparisonInfo(
                reader.GetString(0).Trim(),
                reader.GetString(1).Trim(),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                [.. keys.Zip(candidates, (k, c) => new ReportComparisonMember(k.Trim(), c))],
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return list;
    }

    public async ValueTask<IReadOnlyList<ReportExitSelectionInfo>> ListExitSelectionsAsync(CancellationToken cancellationToken)
    {
        var list = new List<ReportExitSelectionInfo>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectionsSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var record = JsonDocument.Parse(reader.GetString(7));
            list.Add(Map(
                record.RootElement,
                reader.GetString(0).Trim(),
                reader.GetString(1).Trim(),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return list;
    }

    /// <summary>Maps the record of ADR 0054 (schema 1). A member the record lacks reads as absent, never as a zero.</summary>
    internal static ReportExitSelectionInfo Map(JsonElement r, string selectionKey, string comparisonKey, string strategy, string timeframe, string side, string style, string regime, DateTimeOffset createdAt)
    {
        var stability = r.TryGetProperty("stability", out var s) ? s : default;
        return new ReportExitSelectionInfo(
            selectionKey,
            comparisonKey,
            strategy,
            timeframe,
            side,
            style,
            regime,
            Text(r, "unit") ?? string.Empty,
            Text(r, "default") ?? string.Empty,
            Text(r, "chosen"),
            Text(r, "no_choice_reason"),
            stability.ValueKind == JsonValueKind.Object && stability.TryGetProperty("stable", out var stable) && stable.ValueKind is JsonValueKind.True or JsonValueKind.False ? stable.GetBoolean() : null,
            Text(stability, "reason") ?? string.Empty,
            Int(stability, "folds"),
            Int(stability, "agreeing"),
            Int(stability, "positive_out_of_sample"),
            Number(stability, "chance_pass"),
            Int(stability, "lone_peaks"),
            Array(r, "pooled", p => new ReportPooledFigure(Text(p, "name") ?? string.Empty, Int(p, "trades"), Number(p, "mean_r"), Number(p, "score"))),
            Array(r, "folds", f => new ReportSelectionFold(
                Int(f, "index"),
                DateOnly.ParseExact(Text(f, "test_start")!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(Text(f, "test_end")!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Text(f, "winner"),
                Int(f, "winner_test_trades"),
                Number(f, "winner_test_score"),
                f.TryGetProperty("thin", out var thin) && thin.ValueKind == JsonValueKind.True)),
            Array(r, "banner", b => b.GetString() ?? string.Empty),
            createdAt);
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static double? Number(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static List<T> Array<T>(JsonElement e, string name, Func<JsonElement, T> map) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(map)] : [];
}
