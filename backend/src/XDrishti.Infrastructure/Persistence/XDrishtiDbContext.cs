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
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Bars;

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

    /// <summary>The master list; written only by bulk import, read by search.</summary>
    internal DbSet<InstrumentRecord> Instruments => Set<InstrumentRecord>();

    internal DbSet<InstrumentBrokerIdRecord> InstrumentBrokerIds => Set<InstrumentBrokerIdRecord>();

    internal DbSet<Bar1mRecord> Bars1m => Set<Bar1mRecord>();

    internal DbSet<BarAggRecord> BarsAgg => Set<BarAggRecord>();

    internal DbSet<BarCoverageRecord> BarCoverage => Set<BarCoverageRecord>();

    internal DbSet<CsvFileRecord> CsvFiles => Set<CsvFileRecord>();

    /// <summary>Encrypted broker secrets; used only by the broker vault.</summary>
    internal DbSet<BrokerSecretRecord> BrokerSecrets => Set<BrokerSecretRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.HasPostgresExtension("pg_trgm"); // trigram indexes for the instrument search (market schema)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(XDrishtiDbContext).Assembly);
    }
}
