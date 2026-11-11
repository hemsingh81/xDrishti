using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Ai;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;
using XDrishti.Infrastructure.Ai.ReadOnly.DataReader;
using XDrishti.Infrastructure.Lab;
using XDrishti.Infrastructure.Research;

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

        // The typed tools (spec P9-05, ADR 0139): the report engine over the very reader of the Reports screen (under the read login, column grants), and the reviewed views.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IReportTradeSource>(sp => new ReportTradeSource(sp.GetRequiredService<AiDataSources>().Read));
        services.TryAddSingleton<NoTrainingReports>(); // this host never reads a training run, and the read login has no grant on the job tables
        services.TryAddSingleton<ITrainingReportSource>(sp => sp.GetRequiredService<NoTrainingReports>());
        services.TryAddSingleton<ITrainingJobProgress>(sp => sp.GetRequiredService<NoTrainingReports>());
        services.TryAddSingleton(new ReportTrainingOptions());
        services.TryAddSingleton<IChargeSheetConfirmationSource, NoChargeSheetConfirmation>();
        services.TryAddSingleton<IHoldingClassifications, ReportClassifications>();
        services.AddAssistantReportTools();
        services.TryAddSingleton<IAiDataReader, AiDataReader>();
        return services;
    }

    /// <summary>
    /// The two basket reports of <c>get_report</c> over the API (BK-12b, ADR 0267): HTTP-backed implementations of the two BK-07 query interfaces, so that <c>BasketReportReader</c> is unchanged. Settings <c>Mcp:Api</c>; with no address or token the handlers answer <c>tool.unavailable</c> and call nothing.
    /// The one client carries the read-only service token and an allow-list of exactly the two routes.
    /// </summary>
    public static IServiceCollection AddAiBasketApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<BasketApiOptions>()
            .Bind(configuration.GetSection(BasketApiOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.HasValidUrl(), "Mcp:Api:BaseUrl must be an absolute http(s) address.")
            .Validate(o => string.IsNullOrEmpty(o.Token) || !configuration.GetSection("Mcp:Tokens").GetChildren().Any(c => string.Equals(c.Value, o.Token, StringComparison.Ordinal)), "Mcp:Api:Token equals a client token of this server; the service token needs a value of its own.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddTransient<BasketApiEndpointGuard>();
        services.AddHttpClient<BasketApiClient>((sp, client) => client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<BasketApiOptions>>().Value.TimeoutSeconds))
            .AddHttpMessageHandler<BasketApiEndpointGuard>();
        services.TryAddScoped<IQueryHandler<GetBasketProfileQuery, Result<BasketProfileView>>, ApiBasketProfileHandler>();
        services.TryAddScoped<IQueryHandler<GetBasketRankingQuery, Result<BasketRankingView>>, ApiBasketRankingHandler>();
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
