using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>list_runs</c> (spec P9-05 AC 3): simulation runs a report may use (with the default run set marked) or the Strategy Lab runs.</summary>
internal sealed class ListRunsTool(ToolRunner runner, IAiReportReader reports, IAiDataReader data, ILogger<ListRunsTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "list_runs",
        "Stored runs: sim = simulation runs for get_report (run key, strategy, window, default set marked); lab = Strategy Lab runs, newest first, with state and verdict.",
        new ArgDef("kind", ArgType.String, true, "sim or lab.", 3, 3, "^(sim|lab)$"),
        new ArgDef("strategy", ArgType.String, false, "Lab only: strategy id.", 1, 64, "^[A-Za-z][A-Za-z0-9_]*$"),
        new ArgDef("limit", ArgType.Integer, false, "Lab only: 1 to 20, default 10.", 1, 20));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var sim = arguments.String("kind") == "sim";
        var answer = sim
            ? await reports.ListSimRunsAsync(cancellationToken)
            : await data.ListLabRunsAsync(arguments.String("strategy"), arguments.Integer("limit") ?? 10, cancellationToken);
        if (sim)
        {
            logger.Report("runs", answer.ErrorCode ?? "ok", clock.ElapsedMilliseconds);
        }
        else
        {
            logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        }

        return AiToolOutcome.From(answer);
    }
}
