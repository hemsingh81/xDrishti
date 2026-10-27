using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Infrastructure.Lab;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>Wiring of the tool server's read side (spec P9-02): the two logins, the context reader, the catalog, the SQL runner and the strategy validator. The only registration the <c>xd-mcp</c> host makes from the infrastructure: it has no <c>DbContext</c>.</summary>
public static class AiReadOnlyRegistration
{
    public static IServiceCollection AddAiReadOnly(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<ReadOnlyDatabaseOptions>()
            .Bind(configuration.GetSection(ReadOnlyDatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton<AiDataSources>();
        services.TryAddSingleton<IAiContext, AiContextReader>();
        services.TryAddSingleton<IAiCatalog, AiCatalog>();
        services.TryAddSingleton<IAiSqlRunner, AiSqlRunner>();
        services.TryAddSingleton<IStrategyChecker, StrategyChecker>();
        services.TryAddSingleton<IAiLabValidator, LabValidator>();
        return services;
    }

    /// <summary>The probe of the host's health check: can the read login run a trivial query? Never throws.</summary>
    public static async Task<bool> CanReadAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            await services.GetRequiredService<IAiContext>().GetAsync(cancellationToken);
            return true;
        }
#pragma warning disable CA1031 // any failure means "not healthy"
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
