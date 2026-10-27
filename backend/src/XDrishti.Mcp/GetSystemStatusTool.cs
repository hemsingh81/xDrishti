using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_system_status</c> (spec P9-05): a read of the reviewed views through <see cref="IAiDataReader"/>; the runner applies the limits, the cleaning and the envelope.</summary>
internal sealed class GetSystemStatusTool(ToolRunner runner, IAiDataReader data, ILogger<GetSystemStatusTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_system_status",
        "Newest daily bar date, newest plan, last nightly run, service states and data-quality counts, each with as-of. Call before saying today or latest.");

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await data.GetSystemStatusAsync(cancellationToken);
        logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
