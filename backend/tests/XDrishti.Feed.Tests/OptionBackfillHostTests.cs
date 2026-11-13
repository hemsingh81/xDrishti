using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.OptionBackfill;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Feed.Tests;

/// <summary>The expired-options backfill in the xd-feed host (P14-03 criteria 5, 6): off by default in the deployment, its own loop beside the others, paused as a service, its numbers checked at start, the rate never above two a second, the probe a command of its own.</summary>
public sealed class OptionBackfillHostTests
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

        var settings = host.Services.GetRequiredService<OptionBackfillSettings>();

        settings.Enabled.ShouldBeFalse();
        (settings.RequestsPerSecond, settings.MaxRequestsPerDay, settings.Years, settings.StockCount, settings.IndexOffsets, settings.StockOffsets, settings.Timeout).ShouldBe((1.0, 40_000, 5, 20, 10, 3, TimeSpan.FromSeconds(15)));
        settings.MaxStoredBytes.ShouldBe(3L * OptionBackfillSettings.Gb);
        settings.MinFreeBytes.ShouldBe(20L * OptionBackfillSettings.Gb);
        settings.Spacing.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_options_are_bound_from_options_backfill_keys()
    {
        using var host = Builder(new Dictionary<string, string?>
        {
            ["Options:MinFreeGb"] = "30",
            ["Options:Backfill:Enabled"] = "true",
            ["Options:Backfill:RequestsPerSecond"] = "0.5",
            ["Options:Backfill:MaxRequestsPerDay"] = "20000",
            ["Options:Backfill:Years"] = "2",
            ["Options:Backfill:StockCount"] = "5",
            ["Options:Backfill:IndexOffsets"] = "4",
            ["Options:Backfill:StockOffsets"] = "1",
            ["Options:Backfill:TimeoutSeconds"] = "30",
            ["Options:Backfill:MaxStoredGb"] = "8",
        }).Build();

        var settings = host.Services.GetRequiredService<OptionBackfillSettings>();

        settings.Enabled.ShouldBeTrue();
        (settings.RequestsPerSecond, settings.MaxRequestsPerDay, settings.Years, settings.StockCount, settings.IndexOffsets, settings.StockOffsets, settings.Timeout).ShouldBe((0.5, 20_000, 2, 5, 4, 1, TimeSpan.FromSeconds(30)));
        settings.MaxStoredBytes.ShouldBe(8L * OptionBackfillSettings.Gb);
        settings.MinFreeBytes.ShouldBe(30L * OptionBackfillSettings.Gb);
        settings.Spacing.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("Options:Backfill:RequestsPerSecond", "0.1")]
    [InlineData("Options:Backfill:RequestsPerSecond", "2.1")]
    [InlineData("Options:Backfill:RequestsPerSecond", "5")]
    [InlineData("Options:Backfill:MaxRequestsPerDay", "999")]
    [InlineData("Options:Backfill:MaxRequestsPerDay", "90001")]
    [InlineData("Options:Backfill:Years", "0")]
    [InlineData("Options:Backfill:Years", "6")]
    [InlineData("Options:Backfill:StockCount", "-1")]
    [InlineData("Options:Backfill:StockCount", "31")]
    [InlineData("Options:Backfill:IndexOffsets", "11")]
    [InlineData("Options:Backfill:IndexOffsets", "-1")]
    [InlineData("Options:Backfill:StockOffsets", "4")]
    [InlineData("Options:Backfill:TimeoutSeconds", "4")]
    [InlineData("Options:Backfill:TimeoutSeconds", "61")]
    [InlineData("Options:Backfill:MaxStoredGb", "0")]
    [InlineData("Options:Backfill:MaxStoredGb", "51")]
    public async Task A_value_outside_its_range_stops_the_host_at_start(string key, string value)
    {
        using var host = Builder(new Dictionary<string, string?> { [key] = value }).Build();

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_backfill_supervisor_runs_in_its_own_loop_and_every_registration_is_constructible()
    {
        using var host = Builder().Build();

        host.Services.GetRequiredService<IOptionBackfillSupervisor>().ShouldNotBeNull();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptionBackfillClient>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IRollingBarStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IBackfillFetchLog>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOptionBackfillSwitchStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IBackfillProbeAckStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOptionBackfillHeartbeats>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IBackfillTargets>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<BackfillProbe>().ShouldNotBeNull();
        var loops = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();
        loops.ShouldContain("FeedService");
        loops.ShouldContain("OptionChainService");
        loops.ShouldContain("OptionBackfillService");
    }

    [Fact]
    public void The_service_is_registered_once_external_and_paused_and_the_heartbeat_row_is_its_own()
    {
        using var host = Builder().Build();

        host.Services.GetServices<IScheduledService>().Count(s => s.Definition.Id == "options-backfill").ShouldBe(1);
        host.Services.GetRequiredService<ServiceRegistry>().IsExternal("options-backfill").ShouldBeTrue();
        host.Services.GetServices<IScheduledService>().Single(s => s.Definition.Id == "options-backfill").Definition.Defaults.Enabled.ShouldBeFalse();
        IOptionBackfillHeartbeats.ServiceName.ShouldBe("xd-backfill");
    }
}
