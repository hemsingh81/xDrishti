using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Hosting;

/// <summary>One place for cross-cutting host setup, so every executable behaves the same.</summary>
public static class HostDefaults
{
    /// <summary>Directory where container secrets are mounted; file name = configuration key ("Database__Password").</summary>
    public const string SecretsDirectory = "/run/secrets";

    public static TBuilder AddXDrishtiDefaults<TBuilder>(this TBuilder builder, string serviceName)
        where TBuilder : IHostApplicationBuilder
    {
        // Secrets as files (Podman secrets) → configuration. Ignored when the folder does not exist (local dev).
        builder.Configuration.AddKeyPerFile(SecretsDirectory, optional: true, reloadOnChange: false);

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName));

        builder.Services.AddSingleton<IApplicationInfo>(new ApplicationInfo(serviceName, builder.Environment.EnvironmentName));
        return builder;
    }
}
