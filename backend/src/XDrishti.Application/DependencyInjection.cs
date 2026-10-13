using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Application.Auth;
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
using XDrishti.Application.Platform.Heartbeats;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Quotes;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Domain.Common;

namespace XDrishti.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers. Explicit registration: no reflection scanning, fast startup, obvious wiring.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IQueryHandler<GetSystemStatusQuery, SystemStatusResponse>, GetSystemStatusHandler>();
        services.AddScoped<ICommandHandler<RecordHeartbeatCommand>, RecordHeartbeatHandler>();

        services.AddScoped<ICommandHandler<LoginCommand, LoginResult>, LoginHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand>, LogoutHandler>();
        services.AddScoped<ICommandHandler<StepUpCommand, StepUpResult>, StepUpHandler>();
        services.AddScoped<IQueryHandler<VerifySessionQuery, Result<VerifiedSession>>, VerifySessionHandler>();
        services.AddScoped<IQueryHandler<GetAuditQuery, IReadOnlyList<AuditEventResponse>>, GetAuditHandler>();
        services.AddScoped<ICommandHandler<ProvisionOwnerCommand, ProvisionOutcome>, ProvisionOwnerHandler>();
        services.AddScoped<ICommandHandler<ResetOwnerPasswordCommand>, ResetOwnerPasswordHandler>();

        services.AddServices();
        services.AddAccounts();
        services.AddMarket();
        services.AddPortfolio();

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
        services.AddScoped<IQueryHandler<GetHoldingLotsQuery, Result<HoldingLots>>, GetHoldingLotsHandler>();
        services.AddScoped<ICommandHandler<UpdateLotDateCommand, LotRow>, UpdateLotDateHandler>();
        services.AddScoped<IQueryHandler<GetSyncStatusQuery, Result<SyncStatusView>>, GetSyncStatusHandler>();
        services.AddScoped<IAccountSyncSource, AccountSyncSource>();

        // The refresh loop is a singleton: the API's hosted service runs it for the lifetime of the process.
        services.AddSingleton<PortfolioRefreshRunner>();
    }
}
