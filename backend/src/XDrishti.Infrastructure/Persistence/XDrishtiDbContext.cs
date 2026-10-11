using Microsoft.EntityFrameworkCore;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Platform;
using XDrishti.Infrastructure.Accounts;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context. Each module owns a PostgreSQL schema (platform, catalog, market, portfolio, …);
/// mappings live in Configurations/ and are discovered from this assembly.
/// </summary>
public sealed class XDrishtiDbContext(DbContextOptions<XDrishtiDbContext> options) : DbContext(options)
{
    public DbSet<ServiceHeartbeat> ServiceHeartbeats => Set<ServiceHeartbeat>();

    public DbSet<OwnerAccount> OwnerAccounts => Set<OwnerAccount>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<BrokerAccount> BrokerAccounts => Set<BrokerAccount>();

    public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();

    public DbSet<PortfolioBucket> PortfolioBuckets => Set<PortfolioBucket>();

    public DbSet<AccountEvent> AccountEvents => Set<AccountEvent>();

    public DbSet<BrokerConnection> BrokerConnections => Set<BrokerConnection>();

    public DbSet<PendingConnect> PendingConnects => Set<PendingConnect>();

    /// <summary>Encrypted broker secrets; used only by the broker vault.</summary>
    internal DbSet<BrokerSecretRecord> BrokerSecrets => Set<BrokerSecretRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(XDrishtiDbContext).Assembly);
    }
}
