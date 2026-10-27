using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_plan</c> (spec P9-05): a read of the reviewed views through <see cref="IAiDataReader"/>; the runner applies the limits, the cleaning and the envelope.</summary>
internal sealed class GetPlanTool(ToolRunner runner, IAiDataReader data, ILogger<GetPlanTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_plan",
        "Tickets of the stored daily plan for a date (default: newest): symbol, side, state, p-win lower bound, expected R, reasons, stored levels, grade. A plan is a proposal, not an order. Read-only.",
        new ArgDef("date", ArgType.String, false, "yyyy-MM-dd.", 10, 10, "^\\d{4}-\\d{2}-\\d{2}$"),
        new ArgDef("accountLabel", ArgType.String, false, "Account label.", 1, 100));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await data.GetPlanAsync(arguments.String("date"), arguments.String("accountLabel"), cancellationToken);
        logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
