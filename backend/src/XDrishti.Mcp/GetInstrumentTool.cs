using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary><c>get_instrument</c> (spec P9-05): a read of the reviewed views through <see cref="IAiDataReader"/>; the runner applies the limits, the cleaning and the envelope.</summary>
internal sealed class GetInstrumentTool(ToolRunner runner, IAiDataReader data, ILogger<GetInstrumentTool> logger) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "get_instrument",
        "Instrument master facts, daily bar coverage and quality, and the last daily bars of one symbol.",
        new ArgDef("symbol", ArgType.String, true, "NSE symbol.", 1, 40, "^[A-Za-z0-9:._&\\-]{1,40}$"),
        new ArgDef("bars", ArgType.Integer, false, "0 to 60, default 20.", 0, 60));

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var answer = await data.GetInstrumentAsync(arguments.String("symbol")!, arguments.Integer("bars") ?? 20, cancellationToken);
        logger.Data(Spec.Name, answer.Rows?.Count ?? 0, clock.ElapsedMilliseconds);
        return AiToolOutcome.From(answer);
    }
}
