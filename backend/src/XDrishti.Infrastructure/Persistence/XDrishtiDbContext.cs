using Microsoft.EntityFrameworkCore;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;
using XDrishti.Domain.Platform;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Accounts;
using XDrishti.Infrastructure.Learning;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Bars;
using XDrishti.Infrastructure.Market.Live;
using XDrishti.Infrastructure.Planning;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context. Each module owns a PostgreSQL schema (platform, catalog, market, portfolio, …);
/// mappings live in Configurations/ and are discovered from this assembly.
/// </summary>
public sealed class XDrishtiDbContext(DbContextOptions<XDrishtiDbContext> options) : DbContext(options)
{
    public DbSet<ServiceHeartbeat> ServiceHeartbeats => Set<ServiceHeartbeat>();

    public DbSet<ServiceSchedule> ServiceSchedules => Set<ServiceSchedule>();

    public DbSet<ServiceRun> ServiceRuns => Set<ServiceRun>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<OwnerAccount> OwnerAccounts => Set<OwnerAccount>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<BrokerAccount> BrokerAccounts => Set<BrokerAccount>();

    public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();

    public DbSet<PortfolioBucket> PortfolioBuckets => Set<PortfolioBucket>();

    public DbSet<AccountEvent> AccountEvents => Set<AccountEvent>();

    public DbSet<BrokerConnection> BrokerConnections => Set<BrokerConnection>();

    public DbSet<PendingConnect> PendingConnects => Set<PendingConnect>();

    public DbSet<RefreshRun> RefreshRuns => Set<RefreshRun>();

    public DbSet<TrackedInstrument> TrackedInstruments => Set<TrackedInstrument>();

    public DbSet<Basket> Baskets => Set<Basket>();

    public DbSet<BasketMember> BasketMembers => Set<BasketMember>();

    public DbSet<DataJob> DataJobs => Set<DataJob>();

    public DbSet<TradingDay> TradingDays => Set<TradingDay>();

    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();

    public DbSet<BarGap> BarGaps => Set<BarGap>();

    public DbSet<QualityRun> QualityRuns => Set<QualityRun>();

    public DbSet<QualityResult> QualityResults => Set<QualityResult>();

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<HoldingLot> HoldingLots => Set<HoldingLot>();

    public DbSet<HoldingSync> HoldingSyncs => Set<HoldingSync>();

    public DbSet<ActionState> ActionStates => Set<ActionState>();

    public DbSet<PriceAlert> PriceAlerts => Set<PriceAlert>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TradeBatch> TradeBatches => Set<TradeBatch>();

    public DbSet<BrokerSyncRun> BrokerSyncRuns => Set<BrokerSyncRun>();

    public DbSet<BrokerSyncState> BrokerSyncStates => Set<BrokerSyncState>();

    public DbSet<BrokerSyncWindow> BrokerSyncWindows => Set<BrokerSyncWindow>();

    public DbSet<AttentionRecord> AttentionItems => Set<AttentionRecord>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<Dividend> Dividends => Set<Dividend>();

    public DbSet<DividendSuggestion> DividendSuggestions => Set<DividendSuggestion>();

    public DbSet<DividendBatch> DividendBatches => Set<DividendBatch>();

    public DbSet<DividendAlias> DividendAliases => Set<DividendAlias>();

    public DbSet<CostBatch> CostBatches => Set<CostBatch>();

    public DbSet<SecurityAlias> SecurityAliases => Set<SecurityAlias>();

    public DbSet<ReportClientCode> ReportClientCodes => Set<ReportClientCode>();

    /// <summary>The master list; written only by bulk import, read by search.</summary>
    internal DbSet<InstrumentRecord> Instruments => Set<InstrumentRecord>();

    internal DbSet<InstrumentBrokerIdRecord> InstrumentBrokerIds => Set<InstrumentBrokerIdRecord>();

    internal DbSet<Bar1mRecord> Bars1m => Set<Bar1mRecord>();

    internal DbSet<BarAggRecord> BarsAgg => Set<BarAggRecord>();

    internal DbSet<BarCoverageRecord> BarCoverage => Set<BarCoverageRecord>();

    internal DbSet<LiveQuoteRecord> LiveQuotes => Set<LiveQuoteRecord>();

    internal DbSet<CsvFileRecord> CsvFiles => Set<CsvFileRecord>();

    /// <summary>Stored simulation runs (spec P3-10); read by projection, written by the adapter's SQL.</summary>
    internal DbSet<SimRunRow> SimRuns => Set<SimRunRow>();

    internal DbSet<SimTradeRow> SimTrades => Set<SimTradeRow>();

    /// <summary>The legs of stored trades, the charges of their fills, and the comparisons of exits (spec P4-05).</summary>
    internal DbSet<SimTradeLegRow> SimTradeLegs => Set<SimTradeLegRow>();

    internal DbSet<SimTradeLegChargeRow> SimTradeLegCharges => Set<SimTradeLegChargeRow>();

    internal DbSet<SimComparisonRow> SimComparisons => Set<SimComparisonRow>();

    /// <summary>The exit choices and their folds (spec P4-06).</summary>
    internal DbSet<ExitSelectionRow> ExitSelections => Set<ExitSelectionRow>();

    internal DbSet<ExitSelectionFoldRow> ExitSelectionFolds => Set<ExitSelectionFoldRow>();

    /// <summary>The stored strategies and their immutable versions (spec P3-02d).</summary>
    internal DbSet<StrategyRow> Strategies => Set<StrategyRow>();

    internal DbSet<StrategyVersionRow> StrategyVersions => Set<StrategyVersionRow>();

    /// <summary>The Lab runs, which are also the queue of the runner (spec P5-02).</summary>
    internal DbSet<LabRunRow> LabRuns => Set<LabRunRow>();

    internal DbSet<TestLedgerRow> TestLedger => Set<TestLedgerRow>();

    internal DbSet<CellStatsRecord> CellStats => Set<CellStatsRecord>();

    internal DbSet<TradeFeatureRecord> TradeFeatures => Set<TradeFeatureRecord>();

    internal DbSet<TrainingSetRecord> TrainingSets => Set<TrainingSetRecord>();

    internal DbSet<ModelVersionRecord> ModelVersions => Set<ModelVersionRecord>();

    internal DbSet<ModelDesignationRecord> ModelDesignations => Set<ModelDesignationRecord>();
    internal DbSet<LearningChangelogRecord> LearningChangelog => Set<LearningChangelogRecord>();

    internal DbSet<ExperimentRecord> Experiments => Set<ExperimentRecord>();

    internal DbSet<HypothesisRecord> Hypotheses => Set<HypothesisRecord>();

    internal DbSet<LessonRecord> Lessons => Set<LessonRecord>();

    internal DbSet<PlanRecord> Plans => Set<PlanRecord>();

    internal DbSet<PlanItemRecord> PlanItems => Set<PlanItemRecord>();

    internal DbSet<PlanItemAllocationRecord> PlanItemAllocations => Set<PlanItemAllocationRecord>();

    internal DbSet<PlanEventRecord> PlanEvents => Set<PlanEventRecord>();

    internal DbSet<ApprovalRecord> Approvals => Set<ApprovalRecord>();

    internal DbSet<PlanRunRecord> PlanRuns => Set<PlanRunRecord>();

    internal DbSet<GradeRecord> Grades => Set<GradeRecord>();
    internal DbSet<AutonomySettingRecord> AutonomySettings => Set<AutonomySettingRecord>();

    /// <summary>The verdicts of full tests (spec P5-04).</summary>
    internal DbSet<LabResultRow> LabResults => Set<LabResultRow>();

    /// <summary>Encrypted broker secrets; used only by the broker vault.</summary>
    internal DbSet<BrokerSecretRecord> BrokerSecrets => Set<BrokerSecretRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.HasPostgresExtension("pg_trgm"); // trigram indexes for the instrument search (market schema)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(XDrishtiDbContext).Assembly);
    }
}
