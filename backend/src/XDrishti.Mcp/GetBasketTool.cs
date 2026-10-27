using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_basket</c> (spec P9-05): a read of the reviewed views through <see cref="IAiDataReader"/>; the runner applies the limits, the cleaning and the envelope.</summary>
internal sealed class GetBasketTool(ToolRunner runner, IAiDataReader data, ILogger<GetBasketTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_basket",
        "Members of a basket as of today (symbol, name, sector, whether daily bars are stored). Use the exact basket name.",
        new ArgDef("name", ArgType.String, true, "Basket name.", 1, 80));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await data.GetBasketAsync(arguments.String("name")!, cancellationToken);
        logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
