using Microsoft.EntityFrameworkCore;
using XDrishti.Domain.Platform;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context. Each module owns a PostgreSQL schema (platform, catalog, market, portfolio, …);
/// mappings live in Configurations/ and are discovered from this assembly.
/// </summary>
public sealed class XDrishtiDbContext(DbContextOptions<XDrishtiDbContext> options) : DbContext(options)
{
    public DbSet<ServiceHeartbeat> ServiceHeartbeats => Set<ServiceHeartbeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(XDrishtiDbContext).Assembly);
    }
}
