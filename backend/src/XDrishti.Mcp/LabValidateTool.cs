using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp;

/// <summary><c>lab_validate</c> (spec P9-02 AC 10): reads and compiles a strategy text with the Lab's own library and answers whether it is valid, its hash and the located problems. Stores nothing; the tool holds no database connection.</summary>
internal sealed class LabValidateTool(ToolRunner runner, IAiLabValidator validator) : ReadOnlyTool(Spec, runner)
{
    private const int MaxProblems = 30;

    public static readonly ToolSpec Spec = new(
        "lab_validate",
        "Checks a strategy file (YAML text) with the Strategy Lab's own compiler: valid or not, the hash of the strategy, and located problems. Stores nothing and runs nothing. Read-only.",
        new ArgDef("yaml", ArgType.String, true, "The strategy file text.", 1, ToolLimits.MaxYamlChars));

    protected override Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var result = validator.Validate(arguments.String("yaml")!);
        var problems = new JsonArray([.. result.Problems.Take(MaxProblems).Select(p => (JsonNode?)new JsonObject { ["line"] = p.Line, ["column"] = p.Column, ["code"] = p.Code, ["message"] = p.Message, ["severity"] = p.Severity })]);
        return Task.FromResult(new ToolOutcome
        {
            Data = new JsonObject { ["valid"] = result.Valid, ["hash"] = result.Hash, ["problems"] = problems },
            Notes = result.Problems.Count > MaxProblems ? [$"Only the first {MaxProblems} of {result.Problems.Count} problems are shown."] : [],
            Evidence = new ToolEvidence("compiled by the Strategy Lab's strategy-file library; nothing was stored or run", ["lab"], null, result.Problems.Count, [], []),
        });
    }
}
