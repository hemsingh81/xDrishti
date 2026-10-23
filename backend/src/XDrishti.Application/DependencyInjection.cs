using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Application.Auth;
using XDrishti.Application.Lab;
using XDrishti.Application.Learning;
using XDrishti.Application.Live;
using XDrishti.Application.Live.Push;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Calendar;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Planning;
using XDrishti.Application.Platform.Heartbeats;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Dividends;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Application.Portfolio.Live;
using XDrishti.Application.Portfolio.Quotes;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Application.Research;
using XDrishti.Application.Research.CostSheets;
using XDrishti.Application.Selection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers. Explicit registration: no reflection scanning, fast startup, obvious wiring.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IQueryHandler<GetSystemStatusQuery, SystemStatusResponse>, GetSystemStatusHandler>();
        services.AddScoped<ICommandHandler<RecordHeartbeatCommand>, RecordHeartbeatHandler>();
        services.AddScoped<IFeedHeartbeats, FeedHeartbeats>();

        services.AddScoped<ICommandHandler<LoginCommand, LoginResult>, LoginHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand>, LogoutHandler>();
        services.AddScoped<ICommandHandler<StepUpCommand, StepUpResult>, StepUpHandler>();
        services.AddScoped<IQueryHandler<VerifySessionQuery, Result<VerifiedSession>>, VerifySessionHandler>();
        services.AddScoped<IQueryHandler<ListEndedSessionsQuery, IReadOnlyList<Guid>>, ListEndedSessionsHandler>();
        services.AddScoped<IQueryHandler<GetOwnerAccountQuery, Result<AccountResponse>>, GetOwnerAccountHandler>();
        services.AddScoped<ICommandHandler<ChangePasswordCommand>, ChangePasswordHandler>();
        services.AddScoped<ICommandHandler<UpdateProfileCommand, AccountResponse>, UpdateProfileHandler>();
        services.AddScoped<IQueryHandler<GetAuditQuery, IReadOnlyList<AuditEventResponse>>, GetAuditHandler>();
        services.AddScoped<ICommandHandler<ProvisionOwnerCommand, ProvisionOutcome>, ProvisionOwnerHandler>();
        services.AddScoped<ICommandHandler<ResetOwnerPasswordCommand>, ResetOwnerPasswordHandler>();

        services.AddServices();
        services.AddAccounts();
        services.AddMarket();
        services.AddPortfolio();
        services.AddResearch();
        services.AddLab();

        return services;
    }

    /// <summary>
    /// The live feed's engine (ADR 0024) - only for the <c>xd-feed</c> host: the active set (providers, loader), the connection manager, the supervisor that owns the window, the run record and the heartbeat, and their figures. The host provides
    /// <see cref="LiveFeedSettings"/> (from its options) and, through <c>AddInfrastructure</c> and <c>AddLiveFeed</c>, the ports they use.
    /// </summary>
    public static IServiceCollection AddFeedEngine(this IServiceCollection services)
    {
        services.AddScoped<IActiveSetProvider, HoldingsProvider>();
        services.AddScoped<IActiveSetProvider, AlertsProvider>();
        services.AddScoped<IActiveSetProvider, IndicesProvider>();
        services.AddSingleton<MissingIndexReport>();
        services.AddSingleton<ActiveSetLoader>();
        services.AddSingleton<FeedPacketStats>();
        services.AddSingleton<FeedMetrics>();
        services.AddSingleton<ReconnectBackoff>();
        services.AddSingleton<FeedConnectionManager>();
        services.AddSingleton<IFeedSession>(sp => sp.GetRequiredService<FeedConnectionManager>());
        services.AddSingleton<IFeedSupervisor, LiveFeedSupervisor>();
        return services;
    }

    /// <summary>
    /// The SignalR hub's push (ADR 0024 §7, P2-10a) - only for the <c>xd-api</c> host: the coordinator that follows the connected screens (a singleton; the host runs its loop) and the status query. The host provides <see cref="ILiveClients"/>
    /// (the way out to the connections); <c>AddInfrastructure</c> provides the listener, the scope resolver and the price reader.
    /// </summary>
    public static IServiceCollection AddLivePush(this IServiceCollection services)
    {
        services.AddSingleton(PushSettings.Default);
        services.AddSingleton<LivePushCoordinator>();
        services.AddSingleton<ILiveSubscriptions>(sp => sp.GetRequiredService<LivePushCoordinator>());
        services.AddSingleton<ILivePushLoop>(sp => sp.GetRequiredService<LivePushCoordinator>());
        services.AddScoped<IQueryHandler<GetLiveStatusQuery, LiveFeedStatus>, GetLiveStatusHandler>();
        return services;
    }

    /// <summary>
    /// The services framework (ADR 0018): the registry of the services other modules register as <see cref="IScheduledService"/>, the runner and the scheduler (singletons: the API's hosted service runs the
    /// scheduler for the lifetime of the process), the reads of the Services screen and the use cases behind its endpoints.
    /// </summary>
    private static void AddServices(this IServiceCollection services)
    {
        // The services, in the order of the Services screen; each module's work is a singleton that the scheduler runs when its schedule says so.
        services.AddSingleton<IScheduledService, EodService>();
        services.AddSingleton<IScheduledService, InstrumentMasterService>();
        services.AddSingleton<IScheduledService, HoldingQuotesService>();
        services.AddSingleton<IScheduledService, PortfolioBrokerHistoryService>();
        services.AddSingleton<IScheduledService, NightlyService>(); // the fail-closed nightly plan (P7-08); starts paused
        services.AddSingleton<IScheduledService, LiveFeedService>(); // shown and edited here, run by xd-feed (IExternallyRunService): no scheduler starts it
        services.AddSingleton<ServiceRegistry>();
        services.AddSingleton<ServiceRunner>();
        services.AddSingleton<ServiceScheduler>();
        services.AddScoped<ServiceSettingsReader>();
        services.AddScoped<ServiceViewReader>();
        services.AddScoped<ServiceWriter>();
        services.AddScoped<AccountSyncEntry>();
        services.AddScoped<IServiceScheduleReader, ServiceScheduleReader>();

        services.AddScoped<IQueryHandler<ListServicesQuery, ServiceList>, ListServicesHandler>();
        services.AddScoped<IQueryHandler<GetServiceQuery, Result<ServiceView>>, GetServiceHandler>();
        services.AddScoped<IQueryHandler<ListServiceRunsQuery, Result<ServiceRunPage>>, ListServiceRunsHandler>();
        services.AddScoped<IQueryHandler<GetServicesTimelineQuery, Result<ServicesTimeline>>, GetServicesTimelineHandler>();
        services.AddScoped<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>, GetServiceNoticesHandler>();
        services.AddScoped<IQueryHandler<PreviewServiceScheduleQuery, Result<SchedulePreview>>, PreviewServiceScheduleHandler>();
        services.AddScoped<ICommandHandler<SaveServiceScheduleCommand, ServiceView>, SaveServiceScheduleHandler>();
        services.AddScoped<ICommandHandler<ResetServiceScheduleCommand, ServiceView>, ResetServiceScheduleHandler>();
        services.AddScoped<ICommandHandler<PauseServiceCommand, ServiceView>, PauseServiceHandler>();
        services.AddScoped<ICommandHandler<ResumeServiceCommand, ServiceView>, ResumeServiceHandler>();
        services.AddScoped<ICommandHandler<RunServiceNowCommand, ServiceRunView>, RunServiceNowHandler>();
    }

    private static void AddAccounts(this IServiceCollection services)
    {
        services.AddScoped<AccountViews>();
        services.AddScoped<DefaultBuckets>();

        services.AddScoped<IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountSummary>>, ListAccountsHandler>();
        services.AddScoped<IQueryHandler<GetAccountQuery, Result<AccountDetail>>, GetAccountHandler>();
        services.AddScoped<IQueryHandler<GetAccountActivityQuery, Result<IReadOnlyList<AccountEventView>>>, GetAccountActivityHandler>();
        services.AddScoped<ICommandHandler<CreateAccountCommand, AccountDetail>, CreateAccountHandler>();
        services.AddScoped<ICommandHandler<UpdateConnectionCommand, AccountDetail>, UpdateConnectionHandler>();
        services.AddScoped<ICommandHandler<UpdateSecretsCommand, AccountDetail>, UpdateSecretsHandler>();
        services.AddScoped<ICommandHandler<UpdateRolesCommand, AccountDetail>, UpdateRolesHandler>();
        services.AddScoped<ICommandHandler<MakePrimaryDataCommand, AccountDetail>, MakePrimaryDataHandler>();
        services.AddScoped<ICommandHandler<UpdateTradingCommand, AccountDetail>, UpdateTradingHandler>();
        services.AddScoped<ICommandHandler<UpdatePortfolioCommand, PortfolioSaved>, UpdatePortfolioHandler>();
        services.AddScoped<ICommandHandler<UpdateDataCommand, AccountDetail>, UpdateDataHandler>();
        services.AddScoped<ICommandHandler<ArchiveAccountCommand>, ArchiveAccountHandler>();

        services.AddScoped<IQueryHandler<GetDhanRedirectQuery, DhanRedirectView>, GetDhanRedirectHandler>();

        services.AddScoped<ConnectionWriter>();
        services.AddScoped<ICommandHandler<StartConnectCommand, ConnectStartView>, StartConnectHandler>();
        services.AddScoped<ICommandHandler<CompleteConnectCommand, ConnectReturn>, CompleteConnectHandler>();
        services.AddScoped<ICommandHandler<DisconnectCommand, AccountDetail>, DisconnectHandler>();

        services.AddScoped<IQueryHandler<ListRiskProfilesQuery, IReadOnlyList<RiskProfileView>>, ListRiskProfilesHandler>();
        services.AddScoped<ICommandHandler<CreateRiskProfileCommand, RiskProfileView>, CreateRiskProfileHandler>();
        services.AddScoped<ICommandHandler<UpdateRiskProfileCommand, RiskProfileView>, UpdateRiskProfileHandler>();
        services.AddScoped<ICommandHandler<DeleteRiskProfileCommand>, DeleteRiskProfileHandler>();

        services.AddScoped<IQueryHandler<ListBucketsQuery, IReadOnlyList<BucketView>>, ListBucketsHandler>();
        services.AddScoped<ICommandHandler<AddBucketCommand, BucketView>, AddBucketHandler>();
    }

    private static void AddMarket(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<StartRefreshCommand, RefreshStarted>, StartRefreshHandler>();
        services.AddScoped<ICommandHandler<ExecuteRefreshCommand>, ExecuteRefreshHandler>();
        services.AddScoped<ICommandHandler<RecoverInterruptedRefreshesCommand>, RecoverInterruptedRefreshesHandler>();
        services.AddScoped<IQueryHandler<GetRefreshStatusQuery, RefreshStatusView>, GetRefreshStatusHandler>();
        services.AddScoped<IQueryHandler<ListRefreshHistoryQuery, IReadOnlyList<RefreshRunView>>, ListRefreshHistoryHandler>();
        services.AddScoped<IQueryHandler<SearchInstrumentsQuery, Result<InstrumentPage>>, SearchInstrumentsHandler>();
        services.AddScoped<IQueryHandler<GetInstrumentFacetsQuery, InstrumentFacets>, GetInstrumentFacetsHandler>();
        services.AddScoped<IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>>, GetInstrumentHandler>();

        // Tracking: the service is shared with features that track on the owner's behalf (baskets), inside their own unit of work.
        services.AddScoped<InstrumentTracking>();
        services.AddScoped<ICommandHandler<TrackInstrumentsCommand, TrackResult>, TrackInstrumentsHandler>();
        services.AddScoped<ICommandHandler<UntrackInstrumentsCommand, UntrackResult>, UntrackInstrumentsHandler>();
        services.AddScoped<IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>, ResolveInstrumentsHandler>();

        // Baskets (many-to-many, with dated membership). Adding to a basket tracks in the same unit of work, through InstrumentTracking above.
        services.AddScoped<IQueryHandler<ListBasketsQuery, IReadOnlyList<BasketView>>, ListBasketsHandler>();
        services.AddScoped<ICommandHandler<CreateBasketCommand, BasketView>, CreateBasketHandler>();
        services.AddScoped<ICommandHandler<UpdateBasketCommand, BasketView>, UpdateBasketHandler>();
        services.AddScoped<ICommandHandler<ArchiveBasketCommand>, ArchiveBasketHandler>();
        services.AddScoped<ICommandHandler<ReorderBasketsCommand>, ReorderBasketsHandler>();
        services.AddScoped<IQueryHandler<GetBasketMembersQuery, Result<BasketMembers>>, GetBasketMembersHandler>();
        services.AddScoped<IQueryHandler<GetBasketHistoryQuery, Result<BasketHistory>>, GetBasketHistoryHandler>();
        services.AddScoped<IQueryHandler<ListInstrumentBasketsQuery, Result<IReadOnlyList<InstrumentBasketView>>>, ListInstrumentBasketsHandler>();
        services.AddScoped<ICommandHandler<AddBasketMembersCommand, AddMembersResult>, AddBasketMembersHandler>();
        services.AddScoped<ICommandHandler<RemoveBasketMembersCommand, RemoveMembersResult>, RemoveBasketMembersHandler>();
        services.AddScoped<ICommandHandler<ReorderBasketMembersCommand>, ReorderBasketMembersHandler>();
        services.AddScoped<ICommandHandler<TransferBasketMembersCommand, TransferResult>, TransferBasketMembersHandler>();

        services.AddMarketData();
    }

    /// <summary>Market data (ADR 0016): the pipeline (CSV files, Dhan fetch, aggregates), its job records and the reads of the Data &amp; import screen.</summary>
    private static void AddMarketData(this IServiceCollection services)
    {
        services.AddScoped<CsvPhaseRunner>();
        services.AddScoped<CsvUndoRunner>();
        services.AddScoped<DhanPhaseRunner>();
        services.AddScoped<AggregatePhaseRunner>();
        services.AddScoped<GapBackfill>();
        services.AddScoped<QualityPhaseRunner>();

        services.AddScoped<ICommandHandler<StartDataJobCommand, DataJobView>, StartDataJobHandler>();
        services.AddScoped<ICommandHandler<ExecuteDataJobCommand>, ExecuteDataJobHandler>();
        services.AddScoped<ICommandHandler<RecoverInterruptedDataJobsCommand>, RecoverInterruptedDataJobsHandler>();
        services.AddScoped<IQueryHandler<GetDataOverviewQuery, DataOverview>, GetDataOverviewHandler>();
        services.AddScoped<IQueryHandler<ListDataInstrumentsQuery, Result<DataInstrumentPage>>, ListDataInstrumentsHandler>();
        services.AddScoped<IQueryHandler<GetInstrumentBarsQuery, Result<InstrumentBars>>, GetInstrumentBarsHandler>();
        services.AddScoped<IQueryHandler<ListDataJobsQuery, IReadOnlyList<DataJobView>>, ListDataJobsHandler>();
        services.AddScoped<IQueryHandler<GetDataJobQuery, Result<DataJobView>>, GetDataJobHandler>();
        services.AddScoped<IQueryHandler<ListBarGapsQuery, Result<GapPage>>, ListBarGapsHandler>();
        services.AddScoped<IQueryHandler<GetDataNoticesQuery, DataNotices>, GetDataNoticesHandler>();

        // The review step of the CSV load (ADR 0017 §6, P1-07a): the folder's files, the dry run of the real reader, and the undo of a load (a data job).
        services.AddScoped<CsvFileCatalog>();
        services.AddScoped<CsvFilePreviewer>();
        services.AddScoped<IQueryHandler<ListCsvFilesQuery, Result<CsvFilePage>>, ListCsvFilesHandler>();
        services.AddScoped<IQueryHandler<PreviewCsvFilesQuery, Result<CsvPreviewResult>>, PreviewCsvFilesHandler>();
        services.AddScoped<ICommandHandler<UndoCsvLoadCommand, DataJobView>, UndoCsvLoadHandler>();

        // The data quality checks (ADR 0017 §4, P1-14a): the phase runs inside a data job; the rest are its reads and fix actions.
        services.AddScoped<IQueryHandler<GetQualityOverviewQuery, QualityOverview>, GetQualityOverviewHandler>();
        services.AddScoped<IQueryHandler<GetQualityGateQuery, QualityGateView>, GetQualityGateHandler>();
        services.AddScoped<IQueryHandler<ListQualityInstrumentsQuery, Result<QualityInstrumentPage>>, ListQualityInstrumentsHandler>();
        services.AddScoped<IQueryHandler<GetQualityInstrumentQuery, Result<QualityInstrumentDetail>>, GetQualityInstrumentHandler>();
        services.AddScoped<IQueryHandler<ListQualityRunsQuery, IReadOnlyList<QualityRunView>>, ListQualityRunsHandler>();
        services.AddScoped<ICommandHandler<StartQualityRunCommand, DataJobView>, StartQualityRunHandler>();
        services.AddScoped<ICommandHandler<RefetchQualityInstrumentCommand, DataJobView>, RefetchQualityInstrumentHandler>();
        services.AddScoped<ICommandHandler<RefreshQualityAggregatesCommand, DataJobView>, RefreshQualityAggregatesHandler>();

        // The scheduled end-of-day run (ADR 0017 §2) is a service of the framework (ADR 0018, registered with the others in AddServices): it waits for the cleanup of interrupted jobs first.
        services.AddSingleton<DataJobRecovery>();

        // The trading calendar (ADR 0017 §1): the scoped snapshot other slices read, the use cases of its endpoints and the upkeep that follows every data job.
        services.AddScoped<ITradingCalendar, TradingCalendarProvider>();
        services.AddSingleton<HolidayFetchGate>();
        services.AddScoped<NseCalendarRefresher>();
        services.AddScoped<ObservedCalendarRunner>();
        services.AddScoped<ICalendarUpkeep, CalendarUpkeep>();
        services.AddScoped<IQueryHandler<GetCalendarQuery, Result<CalendarView>>, GetCalendarHandler>();
        services.AddScoped<ICommandHandler<AddCalendarDayCommand, AddedCalendarDay>, AddCalendarDayHandler>();
        services.AddScoped<ICommandHandler<ConfirmCalendarDayCommand, CalendarDayView>, ConfirmCalendarDayHandler>();
        services.AddScoped<ICommandHandler<RemoveCalendarDayCommand>, RemoveCalendarDayHandler>();
        services.AddScoped<ICommandHandler<RefreshCalendarCommand, CalendarRefreshResult>, RefreshCalendarHandler>();

        // Corporate actions and price adjustments (ADR 0017 §5, P1-10a): the detection runs after the aggregate phase and on request; adjusted reads use the factors of the applied actions.
        services.AddScoped<CorporateActionDetection>();
        services.AddScoped<IAdjustmentFactors, AdjustmentFactorsProvider>();
        services.AddScoped<IQueryHandler<ListCorporateActionsQuery, Result<CorporateActionPage>>, ListCorporateActionsHandler>();
        services.AddScoped<ICommandHandler<AddCorporateActionCommand, CorporateActionView>, AddCorporateActionHandler>();
        services.AddScoped<ICommandHandler<UpdateCorporateActionCommand, CorporateActionView>, UpdateCorporateActionHandler>();
        services.AddScoped<ICommandHandler<IgnoreCorporateActionCommand, CorporateActionView>, IgnoreCorporateActionHandler>();
        services.AddScoped<ICommandHandler<DetectCorporateActionsCommand, DetectionResult>, DetectCorporateActionsHandler>();
        services.AddScoped<ICommandHandler<ImportCorporateActionsCommand, ImportCorporateActionsResult>, ImportCorporateActionsHandler>();
    }

    private static void AddPortfolio(this IServiceCollection services)
    {
        services.AddScoped<HoldingsFetcher>();
        services.AddScoped<SyncRuns>();
        services.AddScoped<HoldingsImporter>();

        services.AddScoped<IQueryHandler<PreviewHoldingsQuery, Result<ImportPreview>>, PreviewHoldingsHandler>();
        services.AddScoped<ICommandHandler<SyncHoldingsCommand, SyncRunView>, SyncHoldingsHandler>();
        services.AddScoped<ICommandHandler<RecoverInterruptedSyncsCommand>, RecoverInterruptedSyncsHandler>();
        services.AddScoped<IQueryHandler<ListHoldingsQuery, Result<HoldingPage>>, ListHoldingsHandler>();
        services.AddScoped<IQueryHandler<GetHoldingsSummaryQuery, Result<HoldingsSummary>>, GetHoldingsSummaryHandler>();
        services.AddScoped<IQueryHandler<GetHoldingGroupsQuery, Result<HoldingGroups>>, GetHoldingGroupsHandler>();
        services.AddScoped<IQueryHandler<GetHoldingLotsQuery, Result<HoldingLots>>, GetHoldingLotsHandler>();
        services.AddScoped<ICommandHandler<UpdateLotDateCommand, LotRow>, UpdateLotDateHandler>();
        services.AddScoped<IQueryHandler<GetSyncStatusQuery, Result<SyncStatusView>>, GetSyncStatusHandler>();
        services.AddScoped<IAccountSyncSource, AccountSyncSource>();

        // Value history, benchmark and max drawdown (ADR 0020): computed on read from the lots and the stored day bars; the benchmark setting and the two track actions.
        services.AddScoped<IQueryHandler<GetPortfolioHistoryQuery, Result<PortfolioHistory>>, GetPortfolioHistoryHandler>();
        services.AddScoped<IQueryHandler<GetBenchmarkQuery, BenchmarkView>, GetBenchmarkHandler>();
        services.AddScoped<ICommandHandler<SetBenchmarkCommand, BenchmarkView>, SetBenchmarkHandler>();
        services.AddScoped<ICommandHandler<TrackPortfolioHoldingsCommand, PortfolioTrackResult>, TrackPortfolioHoldingsHandler>();
        services.AddScoped<ICommandHandler<TrackBenchmarkCommand, PortfolioTrackResult>, TrackBenchmarkHandler>();

        // The Action center (ADR 0022): rules evaluated on read, the owner's decisions, the thresholds, the price alerts and the notices of the bell. The evaluator, the reconciler, the writer and the checker are shared by several use cases and the quote poll.
        services.AddScoped<ActionEvaluator>();
        services.AddScoped<ActionReconciler>();
        services.AddScoped<ActionDecisions>();
        services.AddScoped<ActionThresholdsWriter>();
        services.AddScoped<PriceAlertChecker>();
        services.AddScoped<IQueryHandler<GetActionItemsQuery, Result<ActionList>>, GetActionItemsHandler>();
        services.AddScoped<ICommandHandler<SnoozeActionCommand, ActionDecisionView>, SnoozeActionHandler>();
        services.AddScoped<ICommandHandler<MarkActionDoneCommand, ActionDecisionView>, MarkActionDoneHandler>();
        services.AddScoped<ICommandHandler<ReopenActionCommand, ActionDecisionView>, ReopenActionHandler>();
        services.AddScoped<IQueryHandler<GetActionThresholdsQuery, ActionThresholdsView>, GetActionThresholdsHandler>();
        services.AddScoped<ICommandHandler<SaveActionThresholdsCommand, ActionThresholdsView>, SaveActionThresholdsHandler>();
        services.AddScoped<ICommandHandler<ResetActionThresholdsCommand, ActionThresholdsView>, ResetActionThresholdsHandler>();
        services.AddScoped<ICommandHandler<CreatePriceAlertCommand, PriceAlertView>, CreatePriceAlertHandler>();
        services.AddScoped<IQueryHandler<ListPriceAlertsQuery, Result<PriceAlertPage>>, ListPriceAlertsHandler>();
        services.AddScoped<ICommandHandler<CancelPriceAlertCommand, PriceAlertView>, CancelPriceAlertHandler>();
        services.AddScoped<IQueryHandler<GetPortfolioNoticesQuery, PortfolioNotices>, GetPortfolioNoticesHandler>();

        // Trades from a tradebook CSV or by hand, and the rebuild of a holding's lots from them (ADR 0023). The workspace is shared by the use cases.
        services.AddScoped<TradeWorkspace>();
        services.AddScoped<IQueryHandler<PreviewTradesQuery, Result<TradePreviewReport>>, PreviewTradesHandler>();
        services.AddScoped<ICommandHandler<ImportTradesCommand, TradeImportResult>, ImportTradesHandler>();
        services.AddScoped<IQueryHandler<ListTradeBatchesQuery, Result<TradeBatchPage>>, ListTradeBatchesHandler>();
        services.AddScoped<ICommandHandler<UndoTradeBatchCommand, TradeUndoResult>, UndoTradeBatchHandler>();
        services.AddScoped<IQueryHandler<ListHoldingTradesQuery, Result<TradePage>>, ListHoldingTradesHandler>();
        services.AddScoped<ICommandHandler<AddManualTradeCommand, TradeView>, AddManualTradeHandler>();
        services.AddScoped<ICommandHandler<VoidTradeCommand, TradeView>, VoidTradeHandler>();
        services.AddScoped<ICommandHandler<RebuildLotsCommand, HoldingRebuildResult>, RebuildLotsHandler>();
        services.AddScoped<IQueryHandler<GetTradeTemplateQuery, string>, GetTradeTemplateHandler>();

        // The trades and the ledger read from Dhan, and the automatic rebuild of lots after a trade sync (ADR 0025). "Sync now" registers the run and a background worker carries it on; the scheduled service (AddServices) uses the waiting form.
        services.AddScoped<BrokerHistoryAccess>();
        services.AddScoped<BrokerHistoryReader>();
        services.AddScoped<BrokerLotRebuilder>();
        services.AddScoped<BrokerHistoryPipeline>();
        services.AddScoped<SyncHealthRecorder>();
        services.AddScoped<PortfolioHealer>();
        services.AddScoped<IAttentionFactsSource, AttentionFactsLoader>();
        services.AddScoped<IAttentionReconciler, AttentionReconciler>();
        services.AddScoped<ICommandHandler<StartBrokerHistoryCommand, BrokerHistoryRunView>, StartBrokerHistoryHandler>();
        services.AddScoped<ICommandHandler<ExecuteBrokerHistoryCommand>, ExecuteBrokerHistoryHandler>();
        services.AddScoped<ICommandHandler<SyncBrokerHistoryCommand, BrokerHistoryRunView>, SyncBrokerHistoryHandler>();
        services.AddScoped<IQueryHandler<GetBrokerHistoryStatusQuery, Result<BrokerHistoryStatusView>>, GetBrokerHistoryStatusHandler>();
        services.AddScoped<IQueryHandler<ListLedgerQuery, Result<LedgerPage>>, ListLedgerHandler>();

        // Dividends entered by the owner, and the suggestions from the ledger (ADR 0026).
        services.AddScoped<ICommandHandler<AddDividendCommand, DividendView>, AddDividendHandler>();
        services.AddScoped<ICommandHandler<VoidDividendCommand, DividendView>, VoidDividendHandler>();
        services.AddScoped<IQueryHandler<ListHoldingDividendsQuery, Result<DividendPage>>, ListHoldingDividendsHandler>();
        services.AddScoped<IQueryHandler<ListAccountDividendsQuery, Result<DividendPage>>, ListAccountDividendsHandler>();
        services.AddScoped<IQueryHandler<ListDividendSuggestionsQuery, Result<DividendSuggestionPage>>, ListDividendSuggestionsHandler>();
        services.AddScoped<ICommandHandler<AcceptDividendSuggestionCommand, DividendView>, AcceptDividendSuggestionHandler>();
        services.AddScoped<ICommandHandler<DismissDividendSuggestionCommand, SuggestionDecisionView>, DismissDividendSuggestionHandler>();

        // Dividends from a payout file: preview, import with choices and remembered names, history, undo (ADR 0028). The workspace is shared by the use cases.
        services.AddScoped<DividendWorkspace>();
        services.AddScoped<IQueryHandler<PreviewDividendsQuery, Result<DividendPreviewReport>>, PreviewDividendsHandler>();
        services.AddScoped<ICommandHandler<ImportDividendsCommand, DividendImportResult>, ImportDividendsHandler>();
        services.AddScoped<IQueryHandler<ListDividendBatchesQuery, Result<DividendBatchPage>>, ListDividendBatchesHandler>();
        services.AddScoped<ICommandHandler<UndoDividendBatchCommand, DividendUndoResult>, UndoDividendBatchHandler>();

        // Dhan's downloaded reports - global transaction report, depository charges, demat holding - recognised by their title and header, previewed and imported with Undo (ADR 0029). The workspace and the services are shared by the use cases.
        services.AddScoped<SyncGuard>();
        services.AddScoped<TradeImporter>();
        services.AddScoped<ReportWorkspace>();
        services.AddScoped<ReportClientCodes>();
        services.AddScoped<BillService>();
        services.AddScoped<DpService>();
        services.AddScoped<DematService>();
        services.AddScoped<ReportPreviewer>();
        services.AddScoped<IQueryHandler<PreviewImportFileQuery, Result<ImportFilePreview>>, PreviewImportFileHandler>();
        services.AddScoped<ICommandHandler<ImportFileCommand, ImportFileResult>, ImportFileHandler>();
        services.AddScoped<IQueryHandler<ListCostBatchesQuery, Result<CostBatchPage>>, ListCostBatchesHandler>();
        services.AddScoped<ICommandHandler<UndoCostBatchCommand, CostUndoResult>, UndoCostBatchHandler>();

        // The live price sink (ADR 0024): a singleton for the lifetime of the feed process; it resolves the scoped ports per flush.
        services.AddSingleton<LiveFlush>();
        services.AddSingleton<ILiveFlush>(sp => sp.GetRequiredService<LiveFlush>());

        // The refresh loop is a singleton: the API's hosted service runs it for the lifetime of the process.
        services.AddSingleton<PortfolioRefreshRunner>();
    }

    /// <summary>The Lab API (P5-01, ADR 0057): validate, store and read strategy texts.</summary>
    private static void AddLab(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ValidateStrategyQuery, StrategyValidation>, ValidateStrategyHandler>();
        services.AddScoped<ICommandHandler<CreateStrategyCommand, CreatedStrategy>, CreateStrategyHandler>();
        services.AddScoped<IQueryHandler<ListStrategiesQuery, IReadOnlyList<StoredStrategy>>, ListStrategiesHandler>();
        services.AddScoped<IQueryHandler<GetStrategyQuery, Result<StrategyDetail>>, GetStrategyHandler>();
        services.AddScoped<IQueryHandler<GetStrategyVersionQuery, Result<StrategyVersionView>>, GetStrategyVersionHandler>();
        services.AddScoped<IQueryHandler<CloneStrategyQuery, Result<ClonedStrategy>>, CloneStrategyHandler>();
        services.AddScoped<IQueryHandler<GetStrategySchemaQuery, string>, GetStrategySchemaHandler>();

        // Lab runs (P5-02, ADR 0058): the API's use cases; the runner that executes them is AddLabRunner, called by the host that runs them.
        services.AddScoped<ICommandHandler<StartLabRunCommand, StartedLabRun>, StartLabRunHandler>();
        services.AddScoped<IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>>, ListLabRunsHandler>();
        services.AddScoped<IQueryHandler<GetLabRunQuery, Result<LabRunView>>, GetLabRunHandler>();
        services.AddScoped<ICommandHandler<CancelLabRunCommand, LabRunView>, CancelLabRunHandler>();
        services.AddScoped<IQueryHandler<GetLabReportQuery, Result<LabReportView>>, GetLabReportHandler>(); // the report of a full or robust run (P5-06, ADR 0063)
        services.AddScoped<IQueryHandler<GetLedgerQuery, Result<LedgerView>>, GetLedgerHandler>(); // the test ledger (P5-03, ADR 0059)
        services.TryAddSingleton(LabSettings.Default); // the host replaces it with its configuration
        services.TryAddSingleton<ILabProgress, NullLabProgress>(); // the host that sends events replaces it
    }

    /// <summary>The executor and the dispatcher of Lab runs (P5-02): only the host that runs them (the API, ADR 0058) calls this; it also registers <see cref="IRunRecordWriter"/>.</summary>
    public static IServiceCollection AddLabRunner(this IServiceCollection services)
    {
        services.AddScoped<ILabRunExecutor, LabRunner>();
        services.AddScoped<LabRobustness>(); // the robustness checks of a robust run (P5-05, ADR 0062)
        services.AddScoped<LabFullTest>(); // the full test of a run (P5-04, ADR 0060)
        services.AddSingleton<LabRunDispatcher>();
        return services;
    }

    /// <summary>Research (P3-04a): running a strategy over the stored bars. The engine behind <see cref="ISimulationEngine"/> is an adapter in Infrastructure (ADR 0039).</summary>
    private static void AddResearch(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>, RunSimulationHandler>();
        services.AddScoped<IQueryHandler<RunComparisonQuery, Result<ComparisonRun>>, RunSimulationHandler>();
        services.AddScoped<IQueryHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>>, BuildTradeCardsHandler>();
        services.AddScoped<ICommandHandler<LoadCostSheetCommand, LoadedCostSheet>, LoadCostSheetHandler>();
        services.AddScoped<IQueryHandler<ShowCostSheetQuery, Result<ShownCostSheet>>, ShowCostSheetHandler>();
        services.AddScoped<IQueryHandler<CheckCostSheetQuery, Result<CostCheckReport>>, CheckCostSheetHandler>();
        services.AddScoped<ICommandHandler<StoreRunCommand, StoreOutcome>, StoreRunHandler>(); // the result store (P3-10)
        services.AddScoped<ICommandHandler<StoreComparisonCommand, StoreOutcome>, StoreComparisonHandler>(); // a comparison of exits (P4-05)
        services.AddScoped<ICommandHandler<StoreExitSelectionCommand, StoreOutcome>, StoreExitSelectionHandler>(); // an exit choice (P4-06)
        services.AddScoped<IQueryHandler<GetStoredRunQuery, Result<StoredRunHeader>>, GetStoredRunHandler>();
        services.AddScoped<IQueryHandler<ListStoredRunsQuery, Result<Page<StoredRunHeader>>>, ListStoredRunsHandler>();
        services.AddScoped<IQueryHandler<PageStoredTradesQuery, Result<Page<RunTrade>>>, PageStoredTradesHandler>();
        services.AddScoped<ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>, ComputeCellStatsHandler>(); // cell statistics, Bayesian and recency (P6-01, ADR 0064)
        services.AddScoped<ICommandHandler<ExtractTradeFeaturesCommand, TradeFeaturesOutcome>, ExtractTradeFeaturesHandler>(); // decision-time features of a stored run (P6-04, ADR 0066)
        services.AddScoped<ICommandHandler<BuildTrainingSetCommand, TrainingSetOutcome>, BuildTrainingSetHandler>(); // the training set: labels joined to features (P6-04)
        services.AddScoped<IQueryHandler<ProposeEntryWindowsQuery, Result<EntryWindowReport>>, ProposeEntryWindowsHandler>(); // learned entry windows, proposal only (P6-14, ADR 0069)
        services.AddScoped<IQueryHandler<ReplayDriftQuery, Result<DriftReport>>, ReplayDriftHandler>(); // drift monitors over replayed history, read-only (P6-09, ADR 0074)
        services.AddScoped<IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>>, AnalyzeLossTagsHandler>(); // automatic loss tags, read-only (P6-15, ADR 0071)
        services.AddScoped<IQueryHandler<ListCellsQuery, Result<IReadOnlyList<CellStatsView>>>, ListCellsHandler>();
        services.AddScoped<IQueryHandler<GetCellQuery, Result<IReadOnlyList<CellStatsView>>>, GetCellHandler>();
        services.AddScoped<IQueryHandler<EvaluateCounterfactualQuery, Result<CounterfactualReport>>, EvaluateCounterfactualHandler>(); // frozen-model counterfactual, evidence only (P6-12, ADR 0077)
        services.AddScoped<IQueryHandler<EvaluateMetaPolicyQuery, Result<MetaPolicyReport>>, EvaluateMetaPolicyHandler>(); // meta-labelling policy of the champion, evidence only (P6-13, ADR 0075)
        services.AddScoped<ICommandHandler<TrainModelCommand, TrainModelOutcome>, TrainModelHandler>(); // train, validate and register a model (P6-07, ADR 0070)
        services.AddScoped<ICommandHandler<DesignateModelCommand, DesignationOutcome>, DesignateModelHandler>(); // champion / challenger / retired, appended (P6-07)
        services.AddScoped<IQueryHandler<GetLearningOverviewQuery, Result<LearningOverviewView>>, GetLearningOverviewHandler>(); // counts and newest stored verdicts for the Learning screen (P6-18, ADR 0080)
        services.AddScoped<IQueryHandler<EvaluatePhaseCheckQuery, Result<PhaseCheckView>>, EvaluatePhaseCheckHandler>(); // the P6 exit criteria from stored records (P6-18, ADR 0080)
        services.AddScoped<IQueryHandler<ShowSelectionQuery, Result<SelectionView>>, ShowSelectionHandler>(); // cell-level view of the P7 selection filters and rank, read-only (P7-01, ADR 0082)
        services.AddScoped<IQueryHandler<ListModelsQuery, Result<IReadOnlyList<ModelView>>>, ListModelsHandler>();
        services.AddScoped<IQueryHandler<GetModelQuery, Result<ModelDetailView>>, GetModelHandler>();
        services.AddScoped<IQueryHandler<ScoreWithModelQuery, Result<ModelScoreView>>, ScoreWithModelHandler>(); // for experiments and P7 (advisory)
        services.AddScoped<ICommandHandler<RunExperimentCommand, RunExperimentOutcome>, RunExperimentHandler>(); // judge a knob change and record it (P6-11, ADR 0072)
        services.AddScoped<IQueryHandler<ListExperimentsQuery, Result<IReadOnlyList<ExperimentView>>>, ListExperimentsHandler>();
        services.AddScoped<IQueryHandler<GetExperimentQuery, Result<ExperimentView>>, GetExperimentHandler>();
        services.AddScoped<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>, RecordCellTransitionHandler>(); // a cell move of doc 09 section 7, as a changelog row (P6-08, ADR 0073)
        services.AddScoped<ICommandHandler<RevertChangelogCommand, ChangelogOutcome>, RevertChangelogHandler>(); // a revert is a new row (P6-08)
        services.AddScoped<IQueryHandler<ListChangelogQuery, Result<IReadOnlyList<ChangelogEntryView>>>, ListChangelogHandler>();
        services.AddScoped<IQueryHandler<GetChangelogEntryQuery, Result<ChangelogEntryView>>, GetChangelogEntryHandler>();
        services.AddScoped<ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome>, ProposeHypothesisHandler>(); // a hypothesis written down: one knob, owner or grid (P6-10, ADR 0076)
        services.AddScoped<ICommandHandler<MoveHypothesisCommand, HypothesisMoveOutcome>, MoveHypothesisHandler>(); // approve, reject, expire, accept by the state table
        services.AddScoped<ICommandHandler<StartHypothesisCommand, HypothesisMoveOutcome>, StartHypothesisHandler>(); // attach the candidate run after the one-variable check
        services.AddScoped<ICommandHandler<JudgeHypothesisCommand, HypothesisMoveOutcome>, JudgeHypothesisHandler>(); // judge with the P6-11 experiment (a trial)
        services.AddScoped<IQueryHandler<ListHypothesesQuery, Result<IReadOnlyList<HypothesisView>>>, ListHypothesesHandler>();
        services.AddScoped<IQueryHandler<GetHypothesisQuery, Result<HypothesisView>>, GetHypothesisHandler>();
        services.AddScoped<IQueryHandler<GridHypothesesQuery, Result<GridHypothesesView>>, GridHypothesesHandler>(); // the deterministic grid source
        services.AddScoped<IQueryHandler<EvaluateHypothesisTriggersQuery, Result<HypothesisTriggersView>>, EvaluateHypothesisTriggersHandler>(); // triggers on replayed data, proposals only
        services.AddScoped<ICommandHandler<OpenLessonsCommand, LessonScanOutcome>, OpenLessonsHandler>(); // lessons opened from the replayed monitors, idempotent (P6-16, ADR 0078)
        services.AddScoped<ICommandHandler<LinkLessonCommand, LessonMoveOutcome>, LinkLessonHandler>(); // link a hypothesis or an experiment
        services.AddScoped<ICommandHandler<CloseLessonCommand, LessonMoveOutcome>, CloseLessonHandler>(); // close with the stored experiment's before and after
        services.AddScoped<ICommandHandler<DismissLessonCommand, LessonMoveOutcome>, DismissLessonHandler>();
        services.AddScoped<IQueryHandler<ListLessonsQuery, Result<IReadOnlyList<LessonView>>>, ListLessonsHandler>();
        services.AddScoped<IQueryHandler<GetLessonQuery, Result<LessonView>>, GetLessonHandler>();
        services.AddScoped<IQueryHandler<GetCellStateQuery, Result<CellStateView>>, GetCellStateHandler>();
        services.AddScoped<IQueryHandler<ListPlansQuery, Result<IReadOnlyList<PlanView>>>, ListPlansHandler>(); // the plan record (P7-03, ADR 0083)
        services.AddScoped<IQueryHandler<GetPlanQuery, Result<PlanView>>, GetPlanHandler>();
        services.AddScoped<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>, MoveTicketHandler>(); // owner decisions and service moves along the ticket table; no path to a broker
        services.AddScoped<ICommandHandler<MovePlanCommand, PlanMoveOutcome>, MovePlanHandler>();
        services.AddScoped<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>, ApplyReviewCutoffHandler>(); // unreviewed tickets lapse; never an approval
        // the fail-closed nightly pipeline (P7-08, ADR 0086): the gates in the order they are read, the builder (the skeleton's null builder until P7-04/P7-09) and the run
        services.AddScoped<INightlyGate, CalendarGate>();
        services.AddScoped<INightlyGate, EodGate>();
        services.AddScoped<INightlyGate, PortfolioSyncGate>();
        services.AddScoped<INightlyGate, FreshDataGate>();
        services.AddScoped<INightlyGate, QualityGate>();
        services.AddScoped<INightlyGate, SelectionInputsGate>();
        services.AddScoped<IPlanBuilder, NullPlanBuilder>();
        services.AddScoped<ICommandHandler<RunNightlyPlanCommand, NightlyRunView>, RunNightlyPlanHandler>();
        services.AddScoped<IQueryHandler<ListPlanRunsQuery, Result<IReadOnlyList<PlanRunView>>>, ListPlanRunsHandler>();
    }
}
