using XDrishti.Application;
using XDrishti.Hosting;
using XDrishti.Infrastructure;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The host of the <c>simulate</c> verb (spec P3-04b2, D2): the generic host with the handlers and the ports and nothing else. No <c>AddApi()</c> (no endpoints, authentication or CSRF), no broker vault key (a web host without one throws at start),
/// and <b>no command-line arguments</b>, so that <c>--key value</c> pairs of the verb never become configuration. The content root is where the web SDK copied <c>appsettings.json</c> (<see cref="AppContext.BaseDirectory"/>), so the default
/// connection string is found whatever the working directory is. The service provider is built with <c>ValidateOnBuild</c> and <c>ValidateScopes</c>: a missing service fails here, not in the middle of a run. The host is built, never started: no
/// hosted service runs. Logs go to stderr, so that stdout holds the banner and the summary only.
/// </summary>
internal static class SimulateHost
{
    /// <summary>The configuration key of the console sink's threshold for writing to stderr (element 0 of <c>Serilog:WriteTo</c> is the console sink of <c>appsettings.json</c>).</summary>
    public const string StandardErrorKey = "Serilog:WriteTo:0:Args:standardErrorFromLevel";

    /// <summary>The level of the EF Core logger: it logs a failed connection with its stack at Error, and the verb prints the failure itself in one line (spec P3-04b2, AC 4: no stack on the console).</summary>
    public const string EntityFrameworkLevelKey = "Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore";

    /// <summary>Builds the host.</summary>
    /// <param name="overrides">Configuration values that win over every other source (tests give the connection string here).</param>
    /// <param name="configure">Replaces or adds services after the registrations (tests give a fake bar store or a hook).</param>
    public static IHost Create(IReadOnlyDictionary<string, string?>? overrides = null, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = null, ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [StandardErrorKey] = "Verbose", [EntityFrameworkLevelKey] = "Fatal" });
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.AddXDrishtiDefaults("xd-simulate");
        builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
        configure?.Invoke(builder.Services);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        return builder.Build();
    }
}
