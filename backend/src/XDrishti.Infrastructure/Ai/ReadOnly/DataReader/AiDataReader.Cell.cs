using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

internal sealed partial class AiDataReader
{
    private const int MinCellTrades = 30;

    public async Task<AiToolData> GetCellAsync(string? key, string? strategy, string? timeframe, string? side, CancellationToken cancellationToken)
    {
        if (key is null && strategy is null && timeframe is null && side is null)
        {
            return AiToolData.Fail(InvalidArguments, "Give a key or at least one of strategy, timeframe, side.");
        }

        const string Latest = "snapshot_key = (SELECT snapshot_key FROM aiv.cell_stats ORDER BY created_at DESC LIMIT 1)";
        var rows = await QueryAsync(
            $"""
            SELECT cell_key, strategy, timeframe, side, style, exit_policy, regime, window_code, costed, n, wins, win_rate_raw, win_rate_posterior, win_rate_lower, win_rate_upper, expectancy_r, profit_factor, as_of
            FROM aiv.cell_stats
            WHERE {Latest}
              AND (@key::text IS NULL OR cell_key = @key) AND (@strategy::text IS NULL OR strategy = @strategy)
              AND (@timeframe::text IS NULL OR lower(timeframe) = lower(@timeframe)) AND (@side::text IS NULL OR lower(side) = lower(@side))
            ORDER BY cell_key, costed, CASE window_code WHEN 'all' THEN 0 WHEN '250d' THEN 1 ELSE 2 END
            LIMIT 400
            """,
            cancellationToken,
            ("key", key),
            ("strategy", strategy),
            ("timeframe", timeframe),
            ("side", side));
        var evidence = new ToolEvidence("stored cell statistics, in-sample (learned from the same trades they describe)", ["aiv.cell_stats"], null, null, [$"a cell with fewer than {MinCellTrades} trades is insufficient and shows no rate", "in-sample: not a forecast"], []);
        if (rows.Count == 0)
        {
            return AiToolData.OfData(new JsonObject { ["state"] = "empty" }, evidence, ["no cell matches"]);
        }

        var cells = rows.Select(r => (string)r[0]!).Distinct(StringComparer.Ordinal).ToList();
        if (cells.Count > 1)
        {
            var listed = rows.GroupBy(r => (string)r[0]!).Take(20).Select(g => (IReadOnlyList<object?>)[g.Key, g.First()[1], g.First()[2], g.First()[3], g.First()[4], g.First()[5], g.First()[6]]).ToList();
            return AiToolData.OfRows(
                [new("cellKey", false), new("strategy", false), new("timeframe", false), new("side", false), new("style", false), new("exit", false), new("regime", false)],
                listed,
                evidence,
                ["narrow the selector"],
                new JsonObject { ["state"] = "ambiguous", ["cells"] = cells.Count },
                cells.Count,
                cells.Count > 20);
        }

        AiToolColumn[] columns = [new("cellKey", false), new("window", false), new("costed", false), new("state", false), new("reason", false), new("n", false), new("wins", false), new("winRate", false), new("winRatePosterior", false), new("winRateLower", false), new("winRateUpper", false), new("expectancyR", false), new("profitFactor", false)];
        var shaped = rows.Select(r =>
        {
            var n = Convert.ToInt32(r[9], System.Globalization.CultureInfo.InvariantCulture);
            var enough = n >= MinCellTrades;
            return (IReadOnlyList<object?>)
            [
                r[0], r[7], r[8], enough ? "value" : "insufficient", enough ? null : $"n {n} is below the minimum of {MinCellTrades}", n, r[10],
                enough ? r[11] : null, enough ? r[12] : null, enough ? r[13] : null, enough ? r[14] : null, enough ? r[15] : null, enough ? r[16] : null,
            ];
        }).ToList();
        return AiToolData.OfRows(
            columns,
            shaped,
            evidence with { N = rows.Max(r => Convert.ToInt32(r[9], System.Globalization.CultureInfo.InvariantCulture)), DateRange = "as of " + rows[0][17] },
            [],
            new JsonObject { ["state"] = "value", ["strategy"] = (string?)rows[0][1], ["timeframe"] = (string?)rows[0][2], ["side"] = (string?)rows[0][3], ["style"] = (string?)rows[0][4], ["exit"] = (string?)rows[0][5], ["regime"] = (string?)rows[0][6] });
    }
}
