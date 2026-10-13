using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Infrastructure.Accounts;
using XDrishti.Infrastructure.Auth;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Actions;
using XDrishti.Infrastructure.Market.Bars;
using XDrishti.Infrastructure.Market.Calendar;
using XDrishti.Infrastructure.Market.Quality;
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
        services.AddScoped<IServiceScheduleStore, ServiceScheduleStore>();
        services.AddScoped<IServiceRunStore, ServiceRunStore>();
        services.AddScoped<IPlatformUnitOfWork, PlatformUnitOfWork>();
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
        services.AddMarketData(configuration);
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

        // The market quote of the holdings (ADR 0019): the guard allows only POST /v2/marketfeed/quote; one request a second, shared by every run (a singleton throttle, apart from the bar fetch's).
        services.AddHttpClient(BrokerHttpClientNames.DhanQuote, BrokerHttp.ConfigureQuote)
            .ConfigurePrimaryHttpMessageHandler(BrokerHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<BrokerEndpointGuard>()
            .RemoveAllLoggers();
        services.AddSingleton<QuoteThrottle>();
        services.AddScoped<IQuoteSource, DhanQuoteClient>();

        // The 1-minute chart data of the Data API (ADR 0016 §7): 15-second timeout, answers up to 16 MB; the guard allows only POST /v2/charts/intraday.
        services.AddHttpClient(BrokerHttpClientNames.DhanChart, BrokerHttp.ConfigureChart)
            .ConfigurePrimaryHttpMessageHandler(BrokerHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<BrokerEndpointGuard>()
            .RemoveAllLoggers();
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
        services.AddScoped<IHoldingQuotes, HoldingQuotes>();
        services.AddScoped<IPortfolioUnitOfWork, PortfolioUnitOfWork>();
        services.AddScoped<IInstrumentMapper, InstrumentMapper>();
    }

    /// <summary>
    /// The instrument master (ADR 0010): its store, search, refresh queue and the allow-listed download clients (the two master files and NSE's holiday list) — no retries, no redirects, no
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
        services.AddScoped<IPortfolioBasketSource, PortfolioBasketSource>();

        services.AddTransient<MasterDataEndpointGuard>();
        services.AddHttpClient(MasterDataHttpClientNames.Dhan, MasterDataHttp.ConfigureDhan)
            .ConfigurePrimaryHttpMessageHandler(MasterDataHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<MasterDataEndpointGuard>()
            .RemoveAllLoggers();
        services.AddHttpClient(MasterDataHttpClientNames.Nse, MasterDataHttp.ConfigureNse)
            .ConfigurePrimaryHttpMessageHandler(MasterDataHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<MasterDataEndpointGuard>()
            .RemoveAllLoggers();
        services.AddHttpClient(MasterDataHttpClientNames.NseApi, MasterDataHttp.ConfigureNseApi)
            .ConfigurePrimaryHttpMessageHandler(MasterDataHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<MasterDataEndpointGuard>()
            .RemoveAllLoggers();
        services.AddScoped<HttpInstrumentSource>();
        services.AddScoped<IInstrumentSource>(sp => sp.GetRequiredService<HttpInstrumentSource>());
        services.AddScoped<IHolidaySource>(sp => sp.GetRequiredService<HttpInstrumentSource>());
    }

    /// <summary>
    /// The market data pipeline (ADR 0016): the stored bars and aggregates, the data job records and queue, the CSV folder and the settings of the Dhan fetch.
    /// Dhan's bars come from <see cref="DhanIntradayBarSource"/> (one request at a time, shared throttle of <c>Data:Dhan:RequestsPerSecond</c>) behind <see cref="IIntradayBarSource"/>.
    /// </summary>
    private static void AddMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DataOptions>()
            .Bind(configuration.GetSection(DataOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(DataOptions.IsValid, DataOptions.InvalidMessage)
            .ValidateOnStart();
        services.AddSingleton<IDataSettings, DataSettings>();
        services.AddSingleton<ICsvFolder, CsvFolder>();
        services.AddSingleton<IDataJobChannel, DataJobChannel>();
        services.AddScoped<IDataJobStore, DataJobStore>();
        services.AddScoped<ICsvFileLog, CsvFileLog>();
        services.AddScoped<IBarStore, BarStore>();
        services.AddScoped<IBarComparison, BarComparer>();
        services.AddScoped<ICsvUndo, CsvUndo>();
        services.AddScoped<IBarAggregates, BarAggregates>();
        services.AddScoped<IBarQueries, BarQueries>();
        services.AddScoped<IBarGaps, BarGapStore>();
        services.AddScoped<IBarDayCounts, BarDayCounts>();
        services.AddScoped<IQualityData, QualityData>();
        services.AddScoped<IQualityRuns, QualityRunStore>();
        services.AddScoped<IQualityQueries, QualityQueries>();
        services.AddScoped<IListedInstruments, ListedInstruments>();
        services.AddScoped<ITradingDayStore, TradingDayStore>();
        services.AddScoped<ICorporateActionStore, CorporateActionStore>();
        services.AddScoped<ICorporateActionQueries, CorporateActionQueries>();
        services.AddScoped<IDailySeries, DailySeries>();
        services.AddScoped<IObservedDays, ObservedDays>();
        services.AddScoped<IFetchTargets, FetchTargets>();
        services.AddScoped<DataAccountStatus>();
        services.AddScoped<IDataAccountStatus>(sp => sp.GetRequiredService<DataAccountStatus>());
        services.AddScoped<IDataAccountCredentials>(sp => sp.GetRequiredService<DataAccountStatus>());
        services.AddSingleton(sp => new RequestThrottle(sp.GetRequiredService<IDataSettings>().DhanRequestsPerSecond, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<DhanIntradayChartClient>();
        services.AddScoped<IIntradayBarSource, DhanIntradayBarSource>();
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
