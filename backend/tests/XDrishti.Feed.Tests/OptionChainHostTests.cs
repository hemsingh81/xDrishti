using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Feed.Tests;

/// <summary>The option-chain recorder in the xd-feed host (P14-02 criteria 6 and 7): off by default in the deployment, its own loop beside the others, paused as a service, its numbers checked at start, the spacing never under three seconds.</summary>
public sealed class OptionChainHostTests
{
    private static HostApplicationBuilder Builder(Dictionary<string, string?>? overrides = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=localhost;Port=5433;Database=xdrishti;Username=xd_app;Password=not-a-real-password",
            ["BrokerVault:Key"] = new string('k', 40),
        });
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        FeedHost.Configure(builder);
        builder.Services.AddFeedService();
        return builder;
    }

    [Fact]
    public void The_deployment_flag_is_off_by_default_and_every_default_is_the_specs()
    {
        using var host = Builder().Build();

        var settings = host.Services.GetRequiredService<OptionChainSettings>();

        settings.Enabled.ShouldBeFalse();
        (settings.IntervalMinutes, settings.Spacing, settings.StrikesEachSide, settings.StockStrikesEachSide, settings.StockExpiries, settings.Timeout).ShouldBe((5, TimeSpan.FromMilliseconds(3000), 30, 15, 2, TimeSpan.FromSeconds(10)));
        settings.MaxStoredBytes.ShouldBe(4L * OptionChainSettings.Gb);
        settings.MinFreeBytes.ShouldBe(20L * OptionChainSettings.Gb);
        settings.IndexExpiries.ShouldBe([new ChainExpiryRule("NSE:NIFTY", 3, 1), new ChainExpiryRule("NSE:BANKNIFTY", 0, 2)]);
    }

    [Fact]
    public void The_options_are_bound_from_options_chain_keys()
    {
        using var host = Builder(new Dictionary<string, string?>
        {
            ["Options:MinFreeGb"] = "30",
            ["Options:Chain:Enabled"] = "true",
            ["Options:Chain:IntervalMinutes"] = "10",
            ["Options:Chain:SpacingMs"] = "6000",
            ["Options:Chain:StrikesEachSide"] = "20",
            ["Options:Chain:StockStrikesEachSide"] = "10",
            ["Options:Chain:StockExpiries"] = "3",
            ["Options:Chain:TimeoutSeconds"] = "5",
            ["Options:Chain:MaxStoredGb"] = "8",
            ["Options:Chain:IndexExpiries:0:Symbol"] = "NSE:FINNIFTY",
            ["Options:Chain:IndexExpiries:0:Weekly"] = "1",
            ["Options:Chain:IndexExpiries:0:Monthly"] = "0",
        }).Build();

        var settings = host.Services.GetRequiredService<OptionChainSettings>();

        settings.Enabled.ShouldBeTrue();
        (settings.IntervalMinutes, settings.Spacing, settings.StrikesEachSide, settings.StockStrikesEachSide, settings.StockExpiries, settings.Timeout).ShouldBe((10, TimeSpan.FromSeconds(6), 20, 10, 3, TimeSpan.FromSeconds(5)));
        settings.MaxStoredBytes.ShouldBe(8L * OptionChainSettings.Gb);
        settings.MinFreeBytes.ShouldBe(30L * OptionChainSettings.Gb);
        settings.IndexExpiries.ShouldBe([new ChainExpiryRule("NSE:FINNIFTY", 1, 0)], "a bound list replaces the default, it does not add to it");
    }

    [Theory]
    [InlineData("Options:MinFreeGb", "4")]
    [InlineData("Options:MinFreeGb", "501")]
    [InlineData("Options:Chain:IntervalMinutes", "4")]
    [InlineData("Options:Chain:IntervalMinutes", "61")]
    [InlineData("Options:Chain:SpacingMs", "2999")]
    [InlineData("Options:Chain:SpacingMs", "0")]
    [InlineData("Options:Chain:SpacingMs", "60001")]
    [InlineData("Options:Chain:StrikesEachSide", "4")]
    [InlineData("Options:Chain:StrikesEachSide", "51")]
    [InlineData("Options:Chain:StockStrikesEachSide", "2")]
    [InlineData("Options:Chain:StockStrikesEachSide", "31")]
    [InlineData("Options:Chain:StockExpiries", "0")]
    [InlineData("Options:Chain:StockExpiries", "4")]
    [InlineData("Options:Chain:TimeoutSeconds", "0")]
    [InlineData("Options:Chain:TimeoutSeconds", "16")]
    [InlineData("Options:Chain:MaxStoredGb", "0")]
    [InlineData("Options:Chain:MaxStoredGb", "101")]
    [InlineData("Options:Chain:IndexExpiries:0:Symbol", "NIFTY")]
    [InlineData("Options:Chain:IndexExpiries:0:Weekly", "7")]
    public async Task A_value_outside_its_range_stops_the_host_at_start(string key, string value)
    {
        using var host = Builder(new Dictionary<string, string?> { [key] = value }).Build();

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_chain_supervisor_runs_in_its_own_loop_and_every_registration_is_constructible()
    {
        using var host = Builder().Build();

        host.Services.GetRequiredService<IOptionChainSupervisor>().ShouldNotBeNull();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptionChainClient>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOptionChainStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOptionChainSwitchStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOptionChainHeartbeats>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IChainTargets>().ShouldNotBeNull();
        var loops = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();
        loops.ShouldContain("FeedService");
        loops.ShouldContain("DepthService");
        loops.ShouldContain("TickService");
        loops.ShouldContain("OptionChainService");
    }

    [Fact]
    public void The_service_is_registered_once_external_and_paused_and_the_heartbeat_row_is_its_own()
    {
        using var host = Builder().Build();

        host.Services.GetServices<IScheduledService>().Count(s => s.Definition.Id == "option-chain-recorder").ShouldBe(1);
        host.Services.GetRequiredService<ServiceRegistry>().IsExternal("option-chain-recorder").ShouldBeTrue();
        host.Services.GetRequiredService<ServiceRegistry>().UsesLiveFeedMarker("option-chain-recorder").ShouldBeFalse();
        host.Services.GetServices<IScheduledService>().Single(s => s.Definition.Id == "option-chain-recorder").Definition.Defaults.Enabled.ShouldBeFalse();
        IOptionChainHeartbeats.ServiceName.ShouldBe("xd-options");
        IOptionChainHeartbeats.ServiceName.ShouldNotBe("xd-ticks");
        IOptionChainHeartbeats.ServiceName.ShouldNotBe("xd-depth");
    }
}
