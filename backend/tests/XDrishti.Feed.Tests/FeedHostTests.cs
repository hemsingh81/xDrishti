using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Feed.Tests;

/// <summary>The wiring of the xd-feed host: the container builds with every registration constructible, the feed's ports are there, and a bad <c>Live:*</c> value or a missing vault key stops it at start.</summary>
public sealed class FeedHostTests
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
    public void The_container_builds_and_every_registration_can_be_constructed()
    {
        using var host = Builder().Build();

        host.Services.GetRequiredService<IFeedSupervisor>().ShouldNotBeNull();
        host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ShouldContain("FeedService");
    }

    [Fact]
    public void The_ports_the_feed_uses_are_all_registered()
    {
        using var host = Builder().Build();
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<ILiveFeedConnector>().ShouldNotBeNull();
        services.GetRequiredService<IActiveSetChanges>().ShouldNotBeNull();
        services.GetRequiredService<ILiveFlush>().ShouldNotBeNull();
        services.GetRequiredService<ILiveQuotes>().ShouldNotBeNull();
        services.GetRequiredService<ILiveNotifier>().ShouldNotBeNull();
        services.GetRequiredService<ILivePrices>().ShouldNotBeNull();
        services.GetRequiredService<IIndexSecurities>().ShouldNotBeNull();
        services.GetRequiredService<IFeedManualRun>().ShouldNotBeNull();
        services.GetRequiredService<IFeedHeartbeats>().ShouldNotBeNull();
        services.GetRequiredService<IDataAccountCredentials>().ShouldNotBeNull();
        services.GetRequiredService<IPriceAlerts>().ShouldNotBeNull();
    }

    [Fact]
    public void The_live_feed_service_is_registered_once_for_the_registry_and_no_scheduler_runs_in_this_host()
    {
        using var host = Builder().Build();

        host.Services.GetServices<IScheduledService>().Count(s => s.Definition.Id == LiveFeedService.Id).ShouldBe(1);
        host.Services.GetRequiredService<ServiceRegistry>().IsExternal(LiveFeedService.Id).ShouldBeTrue();
        host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ShouldContain("FeedService");
        host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ShouldNotContain("ServiceSchedulerHost");
    }

    [Fact]
    public void The_manual_run_lasts_thirty_minutes_unless_the_configuration_says_otherwise()
    {
        using var defaults = Builder().Build();
        using var changed = Builder(new Dictionary<string, string?> { ["Live:ManualRunMinutes"] = "45" }).Build();

        defaults.Services.GetRequiredService<FeedManualRunSettings>().Duration.ShouldBe(TimeSpan.FromMinutes(30));
        changed.Services.GetRequiredService<FeedManualRunSettings>().Duration.ShouldBe(TimeSpan.FromMinutes(45));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("241")]
    public void A_manual_run_outside_one_to_240_minutes_is_refused_when_the_options_are_read(string minutes)
    {
        using var host = Builder(new Dictionary<string, string?> { ["Live:ManualRunMinutes"] = minutes }).Build();

        Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<FeedManualRunSettings>());
    }

    [Fact]
    public void The_host_runs_as_xd_feed()
    {
        using var host = Builder().Build();

        host.Services.GetRequiredService<IApplicationInfo>().Name.ShouldBe("xd-feed");
    }

    [Fact]
    public void The_live_options_are_bound_and_become_the_settings_of_the_feed()
    {
        using var host = Builder(new Dictionary<string, string?> { ["Live:MaxWsInstruments"] = "50", ["Live:FlushSeconds"] = "5" }).Build();

        var settings = host.Services.GetRequiredService<LiveFeedSettings>();

        settings.MaxInstruments.ShouldBe(50);
        settings.FlushInterval.ShouldBe(TimeSpan.FromSeconds(5));
        settings.Silence.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_value_outside_its_bounds_is_refused_when_the_options_are_read()
    {
        using var host = Builder(new Dictionary<string, string?> { ["Live:MaxWsInstruments"] = "0" }).Build();

        Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<LiveOptions>>().Value);
    }

    [Fact]
    public async Task A_bad_option_stops_the_host_at_start()
    {
        using var host = Builder(new Dictionary<string, string?> { ["Live:FlushSeconds"] = "99" }).Build();

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_the_vault_key_the_host_refuses_to_start()
    {
        using var host = Builder(new Dictionary<string, string?> { ["BrokerVault:Key"] = "too short" }).Build();

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }
}
