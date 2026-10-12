using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Infrastructure.Accounts;
using XDrishti.Infrastructure.Auth;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Infrastructure.Platform;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One NpgsqlDataSource per process: owns the connection pool; shared by EF Core and raw ADO.NET.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                CommandTimeout = options.CommandTimeoutSeconds,
                MaxPoolSize = options.MaxPoolSize,
            };
            if (!string.IsNullOrEmpty(options.Password))
            {
                builder.Password = options.Password;
            }

            return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
        });

        // Pooled DbContext instances: avoids per-request allocation cost.
        services.AddDbContextPool<XDrishtiDbContext>((sp, options) =>
            ConfigureDbContext(options, sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddScoped<IServiceHeartbeatStore, ServiceHeartbeatStore>();
        services.AddScoped<IDatabaseInfoProvider, DatabaseInfoProvider>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddScoped<IOwnerAccountStore, OwnerAccountStore>();
        services.AddScoped<ISessionStore, SessionStore>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuthUnitOfWork, AuthUnitOfWork>();

        services.AddOptions<BrokersOptions>()
            .Bind(configuration.GetSection(BrokersOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => BrokersOptions.IsValidBaseUrl(o.PublicBaseUrl), BrokersOptions.InvalidBaseUrlMessage)
            .ValidateOnStart();
        services.AddSingleton<IBrokerSettings, BrokerSettings>();

        // The vault key is checked when the vault is first used; hosts that serve it check it at start (RequireBrokerVaultKey).
        services.AddOptions<BrokerVaultOptions>()
            .Bind(configuration.GetSection(BrokerVaultOptions.SectionName))
            .ValidateDataAnnotations();
        services.AddSingleton(sp => new BrokerSecretCipher(sp.GetRequiredService<IOptions<BrokerVaultOptions>>().Value.Key));
        services.AddScoped<IBrokerSecretVault, BrokerSecretVault>();
        services.AddScoped<IAccountStore, AccountStore>();
        services.AddScoped<IRiskProfileStore, RiskProfileStore>();
        services.AddScoped<IBucketStore, BucketStore>();
        services.AddScoped<IAccountEventLog, AccountEventLog>();
        services.AddScoped<IAccountUnitOfWork, AccountUnitOfWork>();
        services.AddScoped<IBrokerConnectionStore, BrokerConnectionStore>();
        services.AddScoped<IPendingConnectStore, PendingConnectStore>();
        services.AddDhanConnector();
        services.AddInstrumentMaster(configuration);
        services.AddPortfolio(configuration);

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, ["ready"]);

        return services;
    }

    /// <summary>
    /// The Dhan connector and its HTTP client (ADR 0008, 0009): 15-second timeout, no retries (codes are single-use), no redirects, no
    /// request logging (URLs and headers carry tokens and codes), and an allow-list guard in front of every request.
    /// </summary>
    private static void AddDhanConnector(this IServiceCollection services)
    {
        services.AddTransient<BrokerEndpointGuard>();
        services.AddHttpClient(BrokerHttpClientNames.Dhan, BrokerHttp.Configure)
            .ConfigurePrimaryHttpMessageHandler(BrokerHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<BrokerEndpointGuard>()
            .RemoveAllLoggers();

        services.AddScoped<IBrokerConnector, DhanConnector>();

        // The holdings read (ADR 0012) has its own client for its longer timeout and bigger answers; the same guard allows only its one path.
        services.AddHttpClient(BrokerHttpClientNames.DhanHoldings, BrokerHttp.ConfigureHoldings)
            .ConfigurePrimaryHttpMessageHandler(BrokerHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<BrokerEndpointGuard>()
            .RemoveAllLoggers();
        services.AddScoped<IBrokerHoldings, DhanHoldingsClient>();
    }

    /// <summary>The holdings module (ADR 0012): lots and returns inputs, sync records, the unit of work and the automatic refresh setting.</summary>
    private static void AddPortfolio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortfolioRefreshOptions>()
            .Bind(configuration.GetSection(PortfolioRefreshOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPortfolioRefreshSettings, PortfolioRefreshSettings>();
        services.AddScoped<IHoldingStore, HoldingStore>();
        services.AddScoped<IHoldingQueries, HoldingQueries>();
        services.AddScoped<IHoldingSyncStore, HoldingSyncStore>();
        services.AddScoped<IPortfolioUnitOfWork, PortfolioUnitOfWork>();
        services.AddScoped<IInstrumentMapper, InstrumentMapper>();
    }

    /// <summary>
    /// The instrument master (ADR 0010): its store, search, refresh queue and the two allow-listed download clients — no retries, no redirects, no
    /// cookies or credentials, no request logging, and an allow-list guard in front of every request.
    /// </summary>
    private static void AddInstrumentMaster(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InstrumentRefreshOptions>()
            .Bind(configuration.GetSection(InstrumentRefreshOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => InstrumentRefreshOptions.IsValidTime(o.TimeIst), InstrumentRefreshOptions.InvalidTimeMessage)
            .ValidateOnStart();
        services.AddSingleton<IInstrumentRefreshSettings, InstrumentRefreshSettings>();
        services.AddSingleton<IRefreshJobChannel, RefreshJobChannel>();
        services.AddScoped<IRefreshRunStore, RefreshRunStore>();
        services.AddScoped<IInstrumentCatalog, InstrumentCatalog>();
        services.AddScoped<IInstrumentQueries, InstrumentQueries>();
        services.AddScoped<ITrackedInstrumentStore, TrackedInstrumentStore>();
        services.AddScoped<IMarketUnitOfWork, MarketUnitOfWork>();
        services.AddScoped<IBasketStore, BasketStore>();
        services.AddScoped<IBasketQueries, BasketQueries>();

        services.AddTransient<MasterDataEndpointGuard>();
        services.AddHttpClient(MasterDataHttpClientNames.Dhan, MasterDataHttp.ConfigureDhan)
            .ConfigurePrimaryHttpMessageHandler(MasterDataHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<MasterDataEndpointGuard>()
            .RemoveAllLoggers();
        services.AddHttpClient(MasterDataHttpClientNames.Nse, MasterDataHttp.ConfigureNse)
            .ConfigurePrimaryHttpMessageHandler(MasterDataHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<MasterDataEndpointGuard>()
            .RemoveAllLoggers();
        services.AddScoped<IInstrumentSource, HttpInstrumentSource>();
    }

    /// <summary>
    /// Makes the host refuse to start without a valid broker vault key (<c>BrokerVault:Key</c>, at least 32 characters). For hosts that
    /// read or write broker secrets (the API); others never touch the key.
    /// </summary>
    public static IServiceCollection RequireBrokerVaultKey(this IServiceCollection services)
    {
        services.AddOptions<BrokerVaultOptions>().ValidateOnStart();
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        ApplyConventions(options.UseNpgsql(dataSource, Npgsql));

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        ApplyConventions(options.UseNpgsql(connectionString, Npgsql));

    private static void Npgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Platform)
            .EnableRetryOnFailure(maxRetryCount: 3);

    // snake_case tables/columns. Reads opt out of change tracking explicitly with AsNoTracking().
    private static void ApplyConventions(DbContextOptionsBuilder options) =>
        options.UseSnakeCaseNamingConvention();
}
