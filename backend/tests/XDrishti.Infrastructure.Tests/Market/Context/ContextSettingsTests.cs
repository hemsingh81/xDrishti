using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Market.Bars;
using XDrishti.Infrastructure.Market.Context;

namespace XDrishti.Infrastructure.Tests.Market.Context;

public sealed class ContextSettingsTests
{
    private static ContextSettings Of(ContextDataOptions context) => new(Options.Create(new DataOptions { Context = context }));

    [Fact]
    public void Empty_symbol_lists_mean_the_documented_defaults()
    {
        var settings = Of(new ContextDataOptions());

        settings.IndexSymbols.ShouldBe(["NSE:NIFTY"]);
        settings.VixSymbols.ShouldBe(["NSE:INDIA_VIX", "NSE:INDIAVIX"]);
        (settings.Enabled, settings.VixGraceSessions, settings.MinDayFraction).ShouldBe((true, 5, 0.96));
    }

    [Fact]
    public void Configured_symbols_replace_the_defaults()
    {
        var settings = Of(new ContextDataOptions { IndexSymbols = ["NSE:NIFTY_50"], VixSymbols = ["NSE:VIX"], Enabled = false });

        settings.IndexSymbols.ShouldBe(["NSE:NIFTY_50"]);
        settings.VixSymbols.ShouldBe(["NSE:VIX"]);
        settings.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void The_breadth_settings_are_read_from_the_nested_section()
    {
        var settings = Of(new ContextDataOptions { BreadthGraceSessions = 7, Breadth = new BreadthDataOptions { SmaSessions = 10, MinVoters = 30, MinShare = 0.6 } });

        (settings.BreadthSmaSessions, settings.BreadthMinVoters, settings.BreadthMinShare, settings.BreadthGraceSessions).ShouldBe((10, 30, 0.6, 7));
    }
}
