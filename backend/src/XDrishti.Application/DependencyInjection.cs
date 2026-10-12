using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Application.Auth;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Platform.Heartbeats;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Application.Portfolio;
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

        services.AddAccounts();
        services.AddMarket();
        services.AddPortfolio();

        return services;
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

        // The daily schedule is a singleton: the API's hosted service runs it for the lifetime of the process.
        services.AddSingleton<DailyRefreshRunner>();

        services.AddMarketData();
    }

    /// <summary>Market data (ADR 0016): the pipeline (CSV files, Dhan fetch, aggregates), its job records and the reads of the Data &amp; import screen.</summary>
    private static void AddMarketData(this IServiceCollection services)
    {
        services.AddScoped<CsvPhaseRunner>();
        services.AddScoped<DhanPhaseRunner>();
        services.AddScoped<AggregatePhaseRunner>();

        services.AddScoped<ICommandHandler<StartDataJobCommand, DataJobView>, StartDataJobHandler>();
        services.AddScoped<ICommandHandler<ExecuteDataJobCommand>, ExecuteDataJobHandler>();
        services.AddScoped<ICommandHandler<RecoverInterruptedDataJobsCommand>, RecoverInterruptedDataJobsHandler>();
        services.AddScoped<IQueryHandler<GetDataOverviewQuery, DataOverview>, GetDataOverviewHandler>();
        services.AddScoped<IQueryHandler<ListDataInstrumentsQuery, Result<DataInstrumentPage>>, ListDataInstrumentsHandler>();
        services.AddScoped<IQueryHandler<GetInstrumentBarsQuery, Result<InstrumentBars>>, GetInstrumentBarsHandler>();
        services.AddScoped<IQueryHandler<ListDataJobsQuery, IReadOnlyList<DataJobView>>, ListDataJobsHandler>();
        services.AddScoped<IQueryHandler<GetDataJobQuery, Result<DataJobView>>, GetDataJobHandler>();
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

        // The refresh loop is a singleton: the API's hosted service runs it for the lifetime of the process.
        services.AddSingleton<PortfolioRefreshRunner>();
    }
}
