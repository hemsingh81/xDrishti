using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_evidence</c> (AI-01, ADR 0280): the stored replay, readiness and shadow results as the API states them, with their labels and states unchanged. It computes nothing and calls only the read-only evidence port.</summary>
internal sealed class GetEvidenceTool(ToolRunner runner, IAiEvidenceReader evidence, ILogger<GetEvidenceTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_evidence",
        "Stored replay (research only), readiness and shadow results. Quote their labels and states.",
        new ArgDef("kind", ArgType.String, true, "Result.", 1, 20, "^(replay_list|replay_run|readiness_list|readiness_card|shadow_summary)$"),
        new ArgDef("id", ArgType.String, false, "Run id.", 1, 36),
        new ArgDef("strategyId", ArgType.String, false, "Strategy.", 1, 60),
        new ArgDef("version", ArgType.Integer, false, "Version.", 1, 999999));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await evidence.GetEvidenceAsync(arguments.String("kind")!, arguments.String("id"), arguments.String("strategyId"), arguments.Integer("version"), cancellationToken);
        logger.Data(Spec.Name, answer.Data is null ? 0 : 1, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
