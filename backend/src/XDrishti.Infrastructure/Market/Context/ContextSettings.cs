using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Infrastructure.Market.Bars;

namespace XDrishti.Infrastructure.Market.Context;

internal sealed class ContextSettings(IOptions<DataOptions> options) : IContextSettings
{
    private static readonly string[] DefaultIndex = ["NSE:NIFTY"];

    private static readonly string[] DefaultVix = ["NSE:INDIA_VIX", "NSE:INDIAVIX"];

    public bool Enabled => options.Value.Context.Enabled;

    public IReadOnlyList<string> IndexSymbols => options.Value.Context.IndexSymbols is { Length: > 0 } configured ? configured : DefaultIndex;

    public IReadOnlyList<string> VixSymbols => options.Value.Context.VixSymbols is { Length: > 0 } configured ? configured : DefaultVix;

    public int VixGraceSessions => options.Value.Context.VixGraceSessions;

    public double MinDayFraction => options.Value.Context.MinDayFraction;

    public int BreadthSmaSessions => options.Value.Context.Breadth.SmaSessions;

    public int BreadthMinVoters => options.Value.Context.Breadth.MinVoters;

    public double BreadthMinShare => options.Value.Context.Breadth.MinShare;

    public int BreadthGraceSessions => options.Value.Context.BreadthGraceSessions;
}
