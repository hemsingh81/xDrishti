using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

internal sealed partial class AiDataReader
{
    public async Task<AiToolData> ListLabRunsAsync(string? strategy, int limit, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            """
            SELECT r.lab_run_key, r.strategy_id, r.version, r.window_from, r.window_to, r.method, r.state, r.stage, res.outcome, r.created_at
            FROM aiv.lab_runs r
            LEFT JOIN LATERAL (SELECT x.outcome FROM aiv.lab_results x WHERE x.lab_run_key = r.lab_run_key ORDER BY x.created_at DESC LIMIT 1) res ON true
            WHERE (@strategy::text IS NULL OR r.strategy_id = @strategy)
            ORDER BY r.created_at DESC
            LIMIT @n
            """,
            cancellationToken,
            ("strategy", strategy),
            ("n", limit));
        AiToolColumn[] columns = [new("labRunKey", false), new("strategy", false), new("version", false), new("windowFrom", false), new("windowTo", false), new("method", false), new("state", false), new("stage", false), new("verdict", false), new("createdAt", false)];
        return AiToolData.OfRows(
            columns,
            [.. rows.Select(r => (IReadOnlyList<object?>)r)],
            new ToolEvidence("stored Strategy Lab runs", ["aiv.lab_runs", "aiv.lab_results"], null, rows.Count, ["a verdict is shown only where one is stored", "newest first"], []),
            rows.Count == 0 ? ["no Lab run is stored"] : []);
    }

    public async Task<AiToolData> GetLearningAsync(string kind, string? status, int limit, CancellationToken cancellationToken)
    {
        var spec = LearningKinds.Find(kind);
        if (spec is null)
        {
            return AiToolData.Fail(InvalidArguments, "Unknown kind of learning record.");
        }

        var rows = await QueryAsync(spec.Sql, cancellationToken, ("status", status), ("n", limit));
        var data = new JsonObject { ["kind"] = kind };
        return AiToolData.OfRows(
            [.. spec.Columns.Select(c => new AiToolColumn(c.Name, c.IsText))],
            [.. rows.Select(r => (IReadOnlyList<object?>)r)],
            new ToolEvidence($"stored learning records ({kind})", [spec.View], null, rows.Count, ["records are stored state changes, newest first; nothing here moves a state", "cells and experiments are in-sample or out-of-sample as their own fields say"], []),
            rows.Count == 0 ? [$"no {kind} are stored"] : [],
            data);
    }
}
