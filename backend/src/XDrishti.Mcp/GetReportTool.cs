using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_report</c> (spec P9-05 AC 2): the report engine's own answer, gross R only, with each figure as a state and a number only for a value. It computes nothing itself.</summary>
internal sealed class GetReportTool(ToolRunner runner, IAiReportReader reports, ILogger<GetReportTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly string[] QueryKeys = ["runs", "basis", "lens", "strategy", "timeframe", "side", "style", "exit", "basket", "symbol", "from", "to", "metric"];

    public static readonly ToolSpec Spec = new(
        "get_report",
        "Report engine figures from stored simulation runs, gross R only: n, win rate, expectancy per group, each with a state (value, insufficient, unavailable). Prefer it to run_sql for expectancy. query: runs, lens, filters, from, to, metric.",
        new ArgDef("report", ArgType.String, true, "Kind of report.", 1, 20, "^(summary|groups|pivot|what_works|equity|distribution|insights)$"),
        new ArgDef("query", ArgType.StringMap, false, "Report query pairs.", 0, 200, null, 12, QueryKeys));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var kind = arguments.String("report")!;
        var answer = await reports.GetReportAsync(kind, arguments.Map("query"), cancellationToken);
        logger.Report(kind, answer.ErrorCode ?? "ok", clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
