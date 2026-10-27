using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

internal sealed partial class AiDataReader
{
    public async Task<AiToolData> GetSystemStatusAsync(CancellationToken cancellationToken)
    {
        var items = new List<IReadOnlyList<object?>>();
        var unavailable = new List<ToolUnavailable>();

        void Add(string item, object? value, string state, object? asOf, string? reason)
        {
            items.Add([item, value, state, asOf, reason]);
            if (state == "unavailable")
            {
                unavailable.Add(new ToolUnavailable(item, reason ?? "not stored"));
            }
        }

        var bar = await QueryAsync("SELECT max(trade_date) FROM aiv.bars WHERE timeframe = '1D' AND ts > now() - interval '20 days'", cancellationToken);
        if (bar.Count == 1 && bar[0][0] is string barDate)
        {
            Add("newestDailyBar", barDate, "value", barDate, null);
        }
        else
        {
            Add("newestDailyBar", null, "unavailable", null, "no daily bar in the last 20 days");
        }

        var plan = await QueryAsync("SELECT p.for_date, p.version, p.provisional, (SELECT e.to_state FROM aiv.plan_events e WHERE e.plan_key = p.plan_key AND e.item_id IS NULL ORDER BY e.created_at DESC LIMIT 1), p.created_at FROM aiv.plans p ORDER BY p.for_date DESC, p.version DESC LIMIT 1", cancellationToken);
        if (plan.Count == 1)
        {
            var state = plan[0][3] as string ?? "draft";
            Add("newestPlan", $"{plan[0][0]} v{plan[0][1]} {state}{((bool)plan[0][2]! ? " (provisional)" : string.Empty)}", "value", plan[0][4], null);
        }
        else
        {
            Add("newestPlan", null, "unavailable", null, "no plan is stored");
        }

        var nightly = await QueryAsync("SELECT status, started_at, finished_at FROM aiv.service_runs WHERE service_id = 'nightly' ORDER BY started_at DESC LIMIT 1", cancellationToken);
        if (nightly.Count == 1)
        {
            Add("lastNightlyRun", $"{nightly[0][0]}, started {nightly[0][1]}, finished {nightly[0][2] ?? "not yet"}", "value", nightly[0][1], null);
        }
        else
        {
            Add("lastNightlyRun", null, "unavailable", null, "no nightly run is stored");
        }

        var services = await QueryAsync("SELECT DISTINCT ON (service_id) service_id, status, started_at FROM aiv.service_runs ORDER BY service_id, started_at DESC", cancellationToken);
        foreach (var service in services)
        {
            Add("service:" + service[0], service[1], "value", service[2], null);
        }

        if (services.Count == 0)
        {
            Add("services", null, "unavailable", null, "no service run is stored");
        }

        var quality = await QueryAsync("SELECT status, count(*), max(ran_at) FROM aiv.quality_results WHERE run_id = (SELECT run_id FROM aiv.quality_results ORDER BY ran_at DESC LIMIT 1) GROUP BY status ORDER BY status", cancellationToken);
        if (quality.Count > 0)
        {
            Add("dataQualityLatestRun", string.Join(", ", quality.Select(q => $"{q[0]}={q[1]}")), "value", quality[0][2], null);
        }
        else
        {
            Add("dataQualityLatestRun", null, "unavailable", null, "no data-quality run is stored");
        }

        return AiToolData.OfRows(
            [new("item", false), new("value", false), new("state", false), new("asOf", false), new("reason", false)],
            items,
            new ToolEvidence("system status from stored runs", ["aiv.bars", "aiv.plans", "aiv.service_runs", "aiv.quality_results"], null, items.Count, ["call this before saying today or latest; a missing piece is unavailable, not omitted"], unavailable));
    }
}
