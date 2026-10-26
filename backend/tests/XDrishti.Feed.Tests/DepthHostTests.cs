using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Feed.Tests;

/// <summary>The depth recorder in the xd-feed host (P7-16 criteria 6, 8): off by default in the deployment, registered beside the live feed's loop, paused as a service, and its numbers checked at start.</summary>
public sealed class DepthHostTests
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
    public void The_deployment_flag_is_off_by_default_and_the_numbers_have_their_documented_defaults()
    {
        using var host = Builder().Build();

        var settings = host.Services.GetRequiredService<DepthSettings>();

        settings.Enabled.ShouldBeFalse();
        settings.MaxInstruments.ShouldBe(50);
        settings.MaxArmed.ShouldBe(20);
        settings.PinnedSymbols.ShouldBeEmpty();
        settings.Sample.ShouldBe(TimeSpan.FromMilliseconds(1000));
        settings.Flush.ShouldBe(TimeSpan.FromSeconds(5));
        settings.MaxStoredBytes.ShouldBe(40L * DepthSettings.Gb);
        settings.MinFreeBytes.ShouldBe(20L * DepthSettings.Gb);
    }

    [Fact]
    public void The_options_are_bound_from_depth_double_underscore_keys()
    {
        using var host = Builder(new Dictionary<string, string?>
        {
            ["Depth:Enabled"] = "true",
            ["Depth:PinnedSymbols:0"] = "NSE:RELIANCE",
            ["Depth:MaxInstruments"] = "30",
            ["Depth:SampleMs"] = "2000",
            ["Depth:MaxStoredGb"] = "10",
            ["Depth:MinFreeGb"] = "8",
        }).Build();

        var settings = host.Services.GetRequiredService<DepthSettings>();

        settings.Enabled.ShouldBeTrue();
        settings.PinnedSymbols.ShouldBe(["NSE:RELIANCE"]);
        settings.MaxInstruments.ShouldBe(30);
        settings.Sample.ShouldBe(TimeSpan.FromSeconds(2));
        settings.MaxStoredBytes.ShouldBe(10L * DepthSettings.Gb);
        settings.MinFreeBytes.ShouldBe(8L * DepthSettings.Gb);
    }

    [Theory]
    [InlineData("MaxInstruments", "0")]
    [InlineData("MaxInstruments", "51")]
    [InlineData("MaxArmed", "51")]
    [InlineData("SampleMs", "499")]
    [InlineData("SampleMs", "5001")]
    [InlineData("FlushSeconds", "0")]
    [InlineData("FlushSeconds", "31")]
    [InlineData("MaxStoredGb", "0")]
    [InlineData("MaxStoredGb", "501")]
    [InlineData("MinFreeGb", "4")]
    [InlineData("MinFreeGb", "501")]
    public async Task A_value_outside_its_range_stops_the_host_at_start(string key, string value)
    {
        using var host = Builder(new Dictionary<string, string?> { ["Depth:" + key] = value }).Build();

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_depth_supervisor_runs_in_its_own_loop_beside_the_live_feeds_and_every_registration_is_constructible()
    {
        using var host = Builder().Build();

        host.Services.GetRequiredService<IDepthSupervisor>().ShouldNotBeNull();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDepthFeedConnector>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IDepthStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IDepthUniverse>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IDepthSwitchStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IDepthHeartbeats>().ShouldNotBeNull();
        var loops = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();
        loops.ShouldContain("FeedService");
        loops.ShouldContain("DepthService");
        loops.ShouldNotContain("ServiceSchedulerHost");
    }

    [Fact]
    public void The_service_is_registered_once_external_and_paused_and_the_heartbeat_row_is_its_own()
    {
        using var host = Builder().Build();

        host.Services.GetServices<IScheduledService>().Count(s => s.Definition.Id == "depth-recorder").ShouldBe(1);
        host.Services.GetRequiredService<ServiceRegistry>().IsExternal("depth-recorder").ShouldBeTrue();
        host.Services.GetServices<IScheduledService>().Single(s => s.Definition.Id == "depth-recorder").Definition.Defaults.Enabled.ShouldBeFalse();
        IDepthHeartbeats.ServiceName.ShouldBe("xd-depth");
        IDepthHeartbeats.ServiceName.ShouldNotBe(IFeedHeartbeats.ServiceName);
    }
}
