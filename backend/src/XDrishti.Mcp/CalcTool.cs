using System.Text.Json.Nodes;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp;

/// <summary><c>calc</c> (spec P9-05 AC 11): the only source of a derived number. A small parser over decimals (<see cref="Calc"/>); the text is never evaluated by the runtime, and the result is decimal text.</summary>
internal sealed class CalcTool(ToolRunner runner, ILogger<CalcTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "calc",
        "Exact decimal arithmetic for derived numbers (differences, ratios, sums): numbers, + - * / ( ) and round, abs, min, max. Rounds half away from zero to places (default 4). Use it instead of computing yourself.",
        new ArgDef("expression", ArgType.String, true, "For example (0.8-0.6)*100.", 1, Calc.MaxChars),
        new ArgDef("places", ArgType.Integer, false, "0 to 8, default 4.", 0, Calc.MaxPlaces));

    protected override Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var places = arguments.Integer("places") ?? Calc.DefaultPlaces;
        var result = Calc.Evaluate(arguments.String("expression")!, places);
        if (!result.IsSuccess)
        {
            logger.CalcRejected(result.Reason ?? "unknown");
            return Task.FromResult(ToolOutcome.Fail(result.ErrorCode!, result.ErrorCode == "calc.divide_by_zero" ? "The expression divides by zero." : "The expression is not accepted: use numbers, + - * / ( ) and round, abs, min, max, at most 50 tokens."));
        }

        return Task.FromResult(new ToolOutcome
        {
            Data = new JsonObject { ["result"] = result.Text, ["places"] = places, ["operands"] = new JsonArray([.. result.Operands!.Select(o => (JsonNode?)o)]) },
            Evidence = new ToolEvidence("exact decimal arithmetic on the numbers in the expression", [], null, null, ["the result is only as good as the numbers you gave it"], []),
        });
    }
}
