using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Feed.Tests;

/// <summary>The opening-minute recorder in the xd-feed host (BK-03b criteria 1, 7, 11, 12): off by default in the deployment, its own loop beside the other recorders', paused as a service, its numbers checked at start, the free floor shared, the live feed's own set untouched.</summary>
public sealed class OpeningBarsHostTests
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
    public void The_deployment_flag_is_off_by_default_with_the_spec_defaults_and_the_free_floor_of_the_options_recorders()
    {
        using var host = Builder().Build();

        var settings = host.Services.GetRequiredService<OpeningBarsSettings>();

        settings.Enabled.ShouldBeFalse();
        settings.MaxInstruments.ShouldBe(500);
        settings.RecordUntil.ShouldBe(new TimeOnly(9, 31));
        settings.Grace.ShouldBe(TimeSpan.FromSeconds(5));
        settings.FormBy.ShouldBe(TimeSpan.FromMinutes(2));
        settings.KeepDays.ShouldBe(60);
        settings.MinFreeBytes.ShouldBe(host.Services.GetRequiredService<OptionChainSettings>().MinFreeBytes);
    }

    [Fact]
    public void The_options_are_bound_from_opening_bars_keys()
    {
        using var host = Builder(new Dictionary<string, string?>
        {
            ["OpeningBars:Enabled"] = "true",
            ["OpeningBars:MaxInstruments"] = "800",
            ["OpeningBars:RecordUntil"] = "09:35",
            ["OpeningBars:GraceSeconds"] = "10",
        }).Build();

        var settings = host.Services.GetRequiredService<OpeningBarsSettings>();

        settings.Enabled.ShouldBeTrue();
        settings.MaxInstruments.ShouldBe(800);
        settings.RecordUntil.ShouldBe(new TimeOnly(9, 35));
        settings.Grace.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("MaxInstruments", "49")]
    [InlineData("MaxInstruments", "5001")]
    [InlineData("GraceSeconds", "0")]
    [InlineData("GraceSeconds", "31")]
    [InlineData("FormBySeconds", "59")]
    [InlineData("KeepDays", "0")]
    [InlineData("SilenceSeconds", "4")]
    [InlineData("RecordUntil", "09:30")]
    public async Task A_value_outside_its_range_stops_the_host_at_start(string key, string value)
    {
        using var host = Builder(new Dictionary<string, string?> { ["OpeningBars:" + key] = value }).Build();

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_recorder_runs_in_its_own_loop_beside_the_others_and_every_registration_is_constructible()
    {
        using var host = Builder().Build();

        host.Services.GetRequiredService<IOpeningBarsSupervisor>().ShouldNotBeNull();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOpeningBarsFeedConnector>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOpeningBarsStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOpeningBarsQueries>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOpeningBarsSwitchStore>().ShouldNotBeNull();
        var loops = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();
        loops.ShouldContain("FeedService");
        loops.ShouldContain("OpeningBarsHostService");
    }

    [Fact]
    public void The_service_is_registered_once_external_and_paused()
    {
        using var host = Builder().Build();

        host.Services.GetServices<IScheduledService>().Count(s => s.Definition.Id == "opening-bars").ShouldBe(1);
        host.Services.GetRequiredService<ServiceRegistry>().IsExternal("opening-bars").ShouldBeTrue();
        host.Services.GetServices<IScheduledService>().Single(s => s.Definition.Id == "opening-bars").Definition.Defaults.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void The_live_feeds_own_settings_and_set_inputs_are_the_same_with_the_recorder_on_or_off()
    {
        using var off = Builder().Build();
        using var on = Builder(new Dictionary<string, string?> { ["OpeningBars:Enabled"] = "true", ["OpeningBars:MaxInstruments"] = "5000" }).Build();

        var live = off.Services.GetRequiredService<LiveFeedSettings>();
        var liveOn = on.Services.GetRequiredService<LiveFeedSettings>();

        liveOn.ShouldBe(live);
    }
}
