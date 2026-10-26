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
            .AddDepthRecording()
            .AddDepthRecorder();

        // The feed reads the Data account's token from the vault: it refuses to start without the key.
        builder.Services.RequireBrokerVaultKey();
        builder.Services.AddOptions<LiveOptions>()
            .Bind(builder.Configuration.GetSection(LiveOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<LiveOptions>>().Value.ToSettings());

        // The depth recorder (P7-16): off by default (Depth:Enabled false), registered paused, behind the owner's stored switch.
        builder.Services.AddOptions<DepthOptions>()
            .Bind(builder.Configuration.GetSection(DepthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DepthOptions>>().Value.ToSettings());
        return builder;
    }

    public static IServiceCollection AddFeedService(this IServiceCollection services) => services.AddHostedService<FeedService>().AddHostedService<DepthService>();
}
