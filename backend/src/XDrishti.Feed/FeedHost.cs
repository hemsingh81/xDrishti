using Microsoft.Extensions.Options;
using XDrishti.Application;
using XDrishti.Hosting;
using XDrishti.Infrastructure;

namespace XDrishti.Feed;

/// <summary>The wiring of the <c>xd-feed</c> host, in one place so that the host, the health probe and the tests build the same thing.</summary>
internal static class FeedHost
{
    public const string ServiceName = "xd-feed";

    /// <summary>The application and infrastructure layers, the live feed's engine and adapters, and the checked <c>Live:*</c> options. Hosted services are added by <see cref="AddFeedService"/>.</summary>
    public static IHostApplicationBuilder Configure(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddXDrishtiDefaults(ServiceName);
        builder.Services
            .AddApplication()
            .AddInfrastructure(builder.Configuration)
            .AddLiveFeed()
            .AddFeedEngine()
            .AddOptionChainRecording()
            .AddOptionChainRecorder()
            .AddOptionBackfillRecording()
            .AddOptionBackfill()
            .AddOpeningBarsRecording()
            .AddOpeningBarsRecorder();

        // The feed reads the Data account's token from the vault: it refuses to start without the key.
        builder.Services.RequireBrokerVaultKey();
        builder.Services.AddOptions<LiveOptions>()
            .Bind(builder.Configuration.GetSection(LiveOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<LiveOptions>>().Value.ToSettings());

        // The opening-minute recorder (BK-03b): off by default (OpeningBars:Enabled false), registered paused, behind the owner's stored switch baskets.opening_bars; its own connection; the free floor is the options recorders' (Options:MinFreeGb, shared).
        builder.Services.AddOptions<OpeningBarsOptions>()
            .Bind(builder.Configuration.GetSection(OpeningBarsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OpeningBarsOptions>>().Value.ToSettings(sp.GetRequiredService<IOptions<OptionsOptions>>().Value.MinFreeGb));

        // The option-chain recorder (P14-02): off by default (Options:Chain:Enabled false), registered paused, behind its own stored switch; REST requests to the two allowed option-chain endpoints, one per 3 seconds at most.
        builder.Services.AddOptions<OptionsOptions>()
            .Bind(builder.Configuration.GetSection(OptionsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OptionsOptions>>().Value.ToChainSettings());

        // The expired-options backfill (P14-03): off by default (Options:Backfill:Enabled false), registered paused, behind its own stored switch and the owner's acceptance of the probe result; REST requests to the one allowed rolling-option endpoint, one a second at most, only from 16:30 to 08:30.
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OptionsOptions>>().Value.ToBackfillSettings());
        return builder;
    }

    public static IServiceCollection AddFeedService(this IServiceCollection services) => services.AddHostedService<FeedService>().AddHostedService<OptionChainService>().AddHostedService<OptionBackfillService>().AddHostedService<OpeningBarsHostService>();
}
