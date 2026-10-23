using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Infrastructure.Accounts;
using XDrishti.Infrastructure.Auth;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Lab;
using XDrishti.Infrastructure.Learning;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Actions;
using XDrishti.Infrastructure.Market.Bars;
using XDrishti.Infrastructure.Market.Calendar;
using XDrishti.Infrastructure.Market.Live;
using XDrishti.Infrastructure.Market.Quality;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Infrastructure.Planning;
using XDrishti.Infrastructure.Platform;
using XDrishti.Infrastructure.Portfolio;
using XDrishti.Infrastructure.Research;

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
        services.AddScoped<ILiveNotifier, LiveNotifier>();
        services.AddSingleton<ILivePriceChanges, LivePriceChanges>(); // the API's listener on live_prices (ADR 0024 §6)

        // The live feed runs in xd-feed, but the API shows it on the Services screen and its Run now writes the marker the feed reads (ADR 0024 §1).
        services.AddScoped<IFeedManualRun, FeedManualRun>();
        services.AddOptions<LiveManualRunOptions>()
            .Bind(configuration.GetSection(LiveManualRunOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(sp => new FeedManualRunSettings(TimeSpan.FromMinutes(sp.GetRequiredService<IOptions<LiveManualRunOptions>>().Value.ManualRunMinutes)));

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
        services.AddScoped<ICostSheetStore, CostSheetStore>(); // the owner's charge sheets over platform.app_settings (spec P3-03f)
        services.AddScoped<IRunStore, RunStore>(); // the result store of the simulation runs (spec P3-10)
        services.AddScoped<IExitSelectionStore, ExitSelectionStore>(); // the exit choices (spec P4-06)
        services.AddScoped<IStrategyStore, StrategyStore>(); // the stored strategies of the lab schema (spec P3-02d)
        services.AddScoped<ILabResultStore, LabResultStore>(); // the results of full tests (spec P5-04)
        services.AddScoped<ICellStatsStore, CellStatsStore>(); // the cell statistics snapshots (spec P6-01)
        services.AddScoped<ITradeFeatureStore, TradeFeatureStore>(); // the decision-time features of trades (spec P6-04)
        services.AddScoped<ITrainingSetStore, TrainingSetStore>(); // the training-set records (spec P6-04)
        services.AddOptions<ModelStoreOptions>().Bind(configuration.GetSection(ModelStoreOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<ILearningChangelog, LearningChangelog>(); // the insert-only learning changelog (spec P6-08)
        services.AddScoped<IModelRegistry, ModelRegistry>(); // the registered models and their designations (spec P6-07)
        services.AddScoped<IExperimentLedger, ExperimentLedger>(); // the experiment ledger: insert-only, its 30-day count is the trials (spec P6-11)
        services.AddScoped<IHypothesisLog, HypothesisLogStore>(); // the insert-only hypothesis log (spec P6-10)
        services.AddScoped<ILessonLog, LessonLogStore>(); // the insert-only lesson log (spec P6-16)
        services.AddScoped<IPlanStore, PlanStore>(); // the insert-only plan record (spec P7-03, ADR 0083); no order of any kind
        services.AddScoped<IGradeStore, GradeStore>(); // insert-only grades of tickets (P7-13, ADR 0090)
        services.AddSingleton<IGradeSettings, GradeSettings>();
        services.AddScoped<IAutoApprovalStore>(sp => (PlanStore)sp.GetRequiredService<IPlanStore>()); // P7-12: the automatic approval is written by the same store, in the same lock order as an owner's decision
        services.AddScoped<IAutonomySettingsStore, AutonomySettingsStore>(); // the owner's switches, insert-only (P7-12, ADR 0091)
        services.AddSingleton<IPlanBuilderSettings, PlanBuilderSettings>(); // off by default: the empty skeleton stays the nightly builder (P7-04, ADR 0088)
        services.AddScoped<ITicketSource, StoredTicketSource>();
        services.AddScoped<IPlanContextSource, StoredPlanContextSource>(); // accounts, holdings, sectors and correlations as of the close (P7-09, ADR 0092); read only
        services.AddScoped<ISuggestionStore, SuggestionStore>(); // the append-only suggestion inbox (P7-09, ADR 0092)
        services.AddSingleton<IModelFiles, ModelFiles>(); // the model files under data/models, named by hash (spec P6-07)
        services.AddSingleton<IModelTrainer, MlNetModelTrainer>(); // the ML.NET adapter behind its port (ADR 0070): stateless
        services.AddSingleton<IModelScorerLoader, MlNetScorerLoader>();
        services.AddSingleton<ITradeFeatureExtractor, TradeFeatureExtractorAdapter>(); // the feature frame behind its port (ADR 0039): stateless, no I/O
        services.AddScoped<ITestLedger, TestLedger>(); // the per-family count of looks (spec P5-03)
        services.AddScoped<ILabRunStore, LabRunStore>(); // the Lab runs and their queue (spec P5-02)
        services.AddSingleton<IStrategyChecker, StrategyChecker>(); // the strategy file library behind its port for the Lab (spec P5-01): stateless, no I/O
        services.AddScoped<StrategySeeder>(); // the built-ins as text, written by `xd-api migrate`
        services.AddSingleton<ISimulationEngine, SimulationEngineAdapter>(); // the simulation library behind its port (ADR 0039): stateless, no I/O

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

        // The trade history and the ledger (ADR 0025) have the holdings read's limits (30 seconds, answers up to 5 MB); the guard allows only GET /v2/trades/{from}/{to}/{page} and GET /v2/ledger with its two dates. Calls are spaced (a singleton throttle).
        services.AddHttpClient(BrokerHttpClientNames.DhanHistory, BrokerHttp.ConfigureHoldings)
            .ConfigurePrimaryHttpMessageHandler(BrokerHttp.CreatePrimaryHandler)
            .AddHttpMessageHandler<BrokerEndpointGuard>()
            .RemoveAllLoggers();
        services.AddSingleton<DhanHistoryThrottle>();
        services.AddScoped<IBrokerTrades, DhanTradesClient>();
        services.AddScoped<IBrokerLedger, DhanLedgerClient>();

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
        services.AddOptions<BrokerHistoryOptions>()
            .Bind(configuration.GetSection(BrokerHistoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IBrokerHistorySettings, BrokerHistorySettings>();
        services.AddSingleton<IBrokerHistoryChannel, BrokerHistoryChannel>();
        services.AddOptions<HealOptions>()
            .Bind(configuration.GetSection(HealOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IHealSettings, HealSettings>();
        services.AddScoped<ISyncHealthStore, SyncHealthStore>();
        services.AddScoped<IAttentionStore, AttentionStore>();
        services.AddScoped<IAttentionQueries, AttentionQueries>();
        services.AddScoped<IBrokerSyncStore, BrokerSyncStore>();
        services.AddScoped<ILedgerStore, LedgerStore>();
        services.AddScoped<IHoldingStore, HoldingStore>();
        services.AddScoped<IHoldingQueries, HoldingQueries>();
        services.AddScoped<IHoldingClassifications, HoldingClassifications>();
        services.AddScoped<IHoldingSyncStore, HoldingSyncStore>();
        services.AddScoped<ITradeStore, TradeStore>();
        services.AddScoped<IDividendStore, DividendStore>();
        services.AddScoped<IDividendSuggestionStore, DividendSuggestionStore>();
        services.AddScoped<IDividendBatches, DividendBatches>();
        services.AddScoped<IDividendAliases, DividendAliases>();
        services.AddScoped<ISecurityAliases, SecurityAliases>();
        services.AddScoped<IReportClientCodes, ReportClientCodes>();
        services.AddScoped<ICostBatches, CostBatches>();
        services.AddSingleton<ISpreadsheetReader, SpreadsheetReader>();
        services.AddScoped<IInstrumentNames, InstrumentNames>();
        services.AddScoped<IIncomeQueries, IncomeQueries>();
        services.AddScoped<ITradeBatches, TradeBatches>();
        services.AddScoped<ITradeReferenceData, TradeReferenceData>();
        services.AddScoped<IHoldingQuotes, HoldingQuotes>();
        services.AddScoped<ILivePrices, LivePrices>();
        services.AddScoped<ILivePriceReader, LivePriceReader>();
        services.AddScoped<ILiveScopeResolver, LiveScopeResolver>();
        services.AddScoped<IPortfolioUnitOfWork, PortfolioUnitOfWork>();
        services.AddScoped<IActionStates, ActionStates>();
        services.AddScoped<IActionSettings, ActionSettings>();
        services.AddScoped<PriceAlertStore>();
        services.AddScoped<IPriceAlerts>(sp => sp.GetRequiredService<PriceAlertStore>());
        services.AddScoped<IPriceAlertStore>(sp => sp.GetRequiredService<PriceAlertStore>());
        services.AddScoped<IPortfolioPrices, PortfolioPrices>();
        services.AddScoped<IPortfolioBenchmark, PortfolioBenchmarkStore>();
        services.AddSingleton<IPortfolioHistoryCache, PortfolioHistoryCache>();
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
        services.AddScoped<ILiveQuotes, LiveQuotes>();
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
    /// The adapters of the live feed (ADR 0024) - for the <c>xd-feed</c> host: the Dhan feed connector (the WebSocket client behind its allow-list), the <c>LISTEN</c> listener and the index lookup. The sink's own
    /// ports (<c>ILivePrices</c>, <c>ILiveQuotes</c>, <c>ILiveNotifier</c>) are registered with the modules by <see cref="AddInfrastructure"/>.
    /// </summary>
    public static IServiceCollection AddLiveFeed(this IServiceCollection services)
    {
        services.AddSingleton<ILiveFeedConnector, DhanFeedConnector>();
        services.AddSingleton<IActiveSetChanges, ActiveSetChanges>();
        services.AddScoped<IIndexSecurities, IndexSecurities>();
        return services;
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
