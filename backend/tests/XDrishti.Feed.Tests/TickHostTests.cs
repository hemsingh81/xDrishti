using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Feed.Tests;

/// <summary>The trade-tick recorder in the xd-feed host (P7-16b criteria 6, 9): off by default in the deployment, its own loop beside the live feed's and the depth recorder's, paused as a service, its numbers checked at start and the free floor shared.</summary>
public sealed class TickHostTests
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
    public void The_deployment_flag_is_off_by_default_the_cap_is_12_gb_and_the_free_floor_is_the_depth_recorders()
    {
        using var host = Builder().Build();

        var settings = host.Services.GetRequiredService<TickSettings>();

        settings.Enabled.ShouldBeFalse();
        settings.Flush.ShouldBe(TimeSpan.FromSeconds(5));
        settings.MaxStoredBytes.ShouldBe(12L * TickSettings.Gb);
        settings.MinFreeBytes.ShouldBe(host.Services.GetRequiredService<DepthSettings>().MinFreeBytes);
    }

    [Fact]
    public void The_options_are_bound_from_ticks_keys_and_the_floor_follows_depth()
    {
        using var host = Builder(new Dictionary<string, string?>
        {
            ["Ticks:Enabled"] = "true",
            ["Ticks:FlushSeconds"] = "10",
            ["Ticks:MaxStoredGb"] = "20",
            ["Depth:MinFreeGb"] = "30",
        }).Build();

        var settings = host.Services.GetRequiredService<TickSettings>();

        settings.Enabled.ShouldBeTrue();
        settings.Flush.ShouldBe(TimeSpan.FromSeconds(10));
        settings.MaxStoredBytes.ShouldBe(20L * TickSettings.Gb);
        settings.MinFreeBytes.ShouldBe(30L * TickSettings.Gb);
        host.Services.GetRequiredService<DepthSettings>().Enabled.ShouldBeFalse("ticks on does not turn depth on");
    }

    [Theory]
    [InlineData("FlushSeconds", "0")]
    [InlineData("FlushSeconds", "31")]
    [InlineData("MaxStoredGb", "0")]
    [InlineData("MaxStoredGb", "501")]
    public async Task A_value_outside_its_range_stops_the_host_at_start(string key, string value)
    {
        using var host = Builder(new Dictionary<string, string?> { ["Ticks:" + key] = value }).Build();

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_tick_supervisor_runs_in_its_own_loop_and_every_registration_is_constructible()
    {
        using var host = Builder().Build();

        host.Services.GetRequiredService<ITickSupervisor>().ShouldNotBeNull();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITickFeedConnector>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ITickStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ITickSwitchStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ITickHeartbeats>().ShouldNotBeNull();
        var loops = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();
        loops.ShouldContain("FeedService");
        loops.ShouldContain("DepthService");
        loops.ShouldContain("TickService");
    }

    [Fact]
    public void The_service_is_registered_once_external_and_paused_and_the_heartbeat_row_is_its_own()
    {
        using var host = Builder().Build();

        host.Services.GetServices<IScheduledService>().Count(s => s.Definition.Id == "tick-recorder").ShouldBe(1);
        host.Services.GetRequiredService<ServiceRegistry>().IsExternal("tick-recorder").ShouldBeTrue();
        host.Services.GetRequiredService<ServiceRegistry>().UsesLiveFeedMarker("tick-recorder").ShouldBeFalse();
        host.Services.GetServices<IScheduledService>().Single(s => s.Definition.Id == "tick-recorder").Definition.Defaults.Enabled.ShouldBeFalse();
        ITickHeartbeats.ServiceName.ShouldBe("xd-ticks");
        ITickHeartbeats.ServiceName.ShouldNotBe(IFeedHeartbeats.ServiceName);
        ITickHeartbeats.ServiceName.ShouldNotBe(IDepthHeartbeats.ServiceName);
    }
}
