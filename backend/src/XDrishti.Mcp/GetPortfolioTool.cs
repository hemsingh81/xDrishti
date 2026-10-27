using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_portfolio</c> (spec P9-05): a read of the reviewed views through <see cref="IAiDataReader"/>; the runner applies the limits, the cleaning and the envelope.</summary>
internal sealed class GetPortfolioTool(ToolRunner runner, IAiDataReader data, ILogger<GetPortfolioTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_portfolio",
        "Holdings: invested, value, unrealised, XIRR with states, grouped. Unavailable while portfolio context is off. Price-only; no cash figure.",
        new ArgDef("groupBy", ArgType.String, false, "Default none.", 4, 7, "^(none|bucket|basket|sector|account)$"),
        new ArgDef("includeClosed", ArgType.Boolean, false, "Default false."));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await data.GetPortfolioAsync(arguments.String("groupBy") ?? "none", arguments.Boolean("includeClosed") ?? false, cancellationToken);
        logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
