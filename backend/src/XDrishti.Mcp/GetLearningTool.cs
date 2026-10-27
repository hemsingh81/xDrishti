using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_learning</c> (spec P9-05): a read of the reviewed views through <see cref="IAiDataReader"/>; the runner applies the limits, the cleaning and the envelope.</summary>
internal sealed class GetLearningTool(ToolRunner runner, IAiDataReader data, ILogger<GetLearningTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_learning",
        "Stored learning records, newest first: lessons, hypotheses, changelog, experiments or models. Changes no state.",
        new ArgDef("kind", ArgType.String, true, "Which records.", 6, 11, "^(lessons|hypotheses|changelog|experiments|models)$"),
        new ArgDef("status", ArgType.String, false, "State filter.", 1, 40, "^[A-Za-z0-9_\\-]{1,40}$"),
        new ArgDef("limit", ArgType.Integer, false, "1 to 20, default 10.", 1, 20));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await data.GetLearningAsync(arguments.String("kind")!, arguments.String("status"), arguments.Integer("limit") ?? 10, cancellationToken);
        logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
