using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

internal sealed partial class AiDataReader
{
    private static JsonValue? Num(object? value) => value switch { double d => JsonValue.Create(d), int i => JsonValue.Create(i), long l => JsonValue.Create(l), _ => null };

    private const string AccountLabelOff = "unavailable (portfolio context is off)";

    public async Task<AiToolData> GetPlanAsync(string? date, string? accountLabel, CancellationToken cancellationToken)
    {
        DateOnly? day = null;
        if (date is not null)
        {
            if (!TryDay(date, out var parsed))
            {
                return AiToolData.Fail(InvalidArguments, "date must be written yyyy-MM-dd.");
            }

            day = parsed;
        }

        var state = await context.GetAsync(cancellationToken);
        var plans = await QueryAsync(
            "SELECT plan_key, for_date, version, provisional, basis, notice, limits, created_at FROM aiv.plans WHERE (@d::date IS NULL OR for_date = @d) ORDER BY for_date DESC, version DESC LIMIT 1",
            cancellationToken,
            ("d", day));
        var evidence = new ToolEvidence("stored daily plan (a proposal, never an order)", ["aiv.plans", "aiv.plan_items", "aiv.plan_events", "aiv.grades"], null, null, ["a plan is a stored proposal: the engine and the owner decide", "levels are shown only where the plan stored them"], []);
        var account = state.PortfolioAllowed ? accountLabel : (accountLabel is null ? null : AccountLabelOff);
        if (plans.Count == 0)
        {
            return AiToolData.OfData(new JsonObject { ["state"] = "empty", ["date"] = day is null ? null : Day(day.Value), ["accountLabel"] = account }, evidence, ["no plan is stored for that date"]);
        }

        var plan = plans[0];
        var planKey = (string)plan[0]!;
        var items = await QueryAsync(
            """
            SELECT i.item_id, i.item_rank, i.item_group, i.symbol, i.side, i.strategy_id,
                   COALESCE((SELECT e.to_state FROM aiv.plan_events e WHERE e.plan_key = i.plan_key AND e.item_id = i.item_id ORDER BY e.created_at DESC LIMIT 1), 'proposed') AS state,
                   i.p_win_lower, i.conservative_expectancy_r, i.trades, i.reasons, i.unavailable, i.trigger_text, i.stop_text, i.target_text, i.validity_text,
                   i.entry_ref_price, i.levels_session, i.stop_anchor, i.stop_level, i.target_level, i.reward_risk,
                   g.status, g.r, g.exit_reason
            FROM aiv.plan_items i
            LEFT JOIN aiv.grades g ON g.plan_key = i.plan_key AND g.item_id = i.item_id
            WHERE i.plan_key = @pk
            ORDER BY i.item_rank
            LIMIT 200
            """,
            cancellationToken,
            ("pk", planKey));

        var fills = new Dictionary<object, string>();
        var allocations = new Dictionary<object, List<string>>();
        if (state.PortfolioAllowed)
        {
            foreach (var f in await QueryAsync("SELECT e.item_id, f.status, f.r FROM aiv.journal_entries e JOIN aiv.fill_grades f ON f.entry_id = e.entry_id WHERE e.plan_key = @pk", cancellationToken, ("pk", planKey)))
            {
                fills[f[0]!] = $"{f[1]}{(f[2] is double r ? $" r={r:0.####}" : string.Empty)}";
            }

            if (accountLabel is not null)
            {
                foreach (var a in await QueryAsync("SELECT item_id, mode, quantity FROM aiv.plan_item_allocations WHERE plan_key = @pk AND account = @a", cancellationToken, ("pk", planKey), ("a", accountLabel)))
                {
                    if (!allocations.TryGetValue(a[0]!, out var list))
                    {
                        allocations[a[0]!] = list = [];
                    }

                    list.Add($"{a[1]} {a[2]}");
                }
            }
        }

        AiToolColumn[] columns =
        [
            new("ticketId", false), new("rank", false), new("group", false), new("symbol", false), new("side", false), new("strategy", false), new("state", false), new("pWinLower", false), new("expectedR", false), new("trades", false),
            new("reasons", true), new("unavailable", true), new("trigger", true), new("stop", true), new("target", true), new("validity", true), new("levels", false), new("grade", false), new("fillGrade", false), new("allocation", false),
        ];
        var rows = items.Select(r =>
        {
            object? levels;
            if (r[16] is null)
            {
                levels = "not stored";
            }
            else
            {
                levels = new JsonObject { ["entryRef"] = Num(r[16]), ["session"] = (string?)r[17], ["stopAnchor"] = (string?)r[18], ["stop"] = Num(r[19]), ["target"] = Num(r[20]), ["rewardRisk"] = Num(r[21]) };
            }

            object? grade = r[22] is null ? null : $"{r[22]}{(r[23] is double g ? $" r={g:0.####}" : string.Empty)}{(r[24] is string x ? $" exit={x}" : string.Empty)}";
            return (IReadOnlyList<object?>)[r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8], r[9], r[10], r[11], r[12], r[13], r[14], r[15], levels, grade, fills.GetValueOrDefault(r[0]!), allocations.TryGetValue(r[0]!, out var al) ? string.Join("; ", al) : null];
        }).ToList();

        return AiToolData.OfRows(
            columns,
            rows,
            evidence with { N = rows.Count, DateRange = Day(DateOnly.Parse((string)plan[1]!, System.Globalization.CultureInfo.InvariantCulture)) },
            [],
            new JsonObject
            {
                ["state"] = "value",
                ["planKey"] = planKey,
                ["forDate"] = (string?)plan[1],
                ["version"] = Num(plan[2]),
                ["provisional"] = (bool)plan[3]!,
                ["basisText"] = (string?)plan[4],
                ["noticeText"] = (string?)plan[5],
                ["limitsText"] = (string?)plan[6],
                ["accountLabel"] = account,
            });
    }
}
