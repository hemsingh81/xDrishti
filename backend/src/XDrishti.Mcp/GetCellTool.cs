using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_cell</c> (spec P9-05): a read of the reviewed views through <see cref="IAiDataReader"/>; the runner applies the limits, the cleaning and the envelope.</summary>
internal sealed class GetCellTool(ToolRunner runner, IAiDataReader data, ILogger<GetCellTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_cell",
        "Learned statistics of one cell, by key or strategy, timeframe, side: n, wins, win rate, posterior per window (all, 250d, 60d). Under 30 trades: insufficient, no rate.",
        new ArgDef("key", ArgType.String, false, "Cell key.", 1, 200),
        new ArgDef("strategy", ArgType.String, false, "Strategy id.", 1, 64, "^[A-Za-z][A-Za-z0-9_]*$"),
        new ArgDef("timeframe", ArgType.String, false, "For example 15m.", 2, 3, "^[0-9A-Za-z]{2,3}$"),
        new ArgDef("side", ArgType.String, false, "BUY or SELL.", 3, 4, "^(BUY|SELL)$"));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await data.GetCellAsync(arguments.String("key"), arguments.String("strategy"), arguments.String("timeframe"), arguments.String("side"), cancellationToken);
        logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
