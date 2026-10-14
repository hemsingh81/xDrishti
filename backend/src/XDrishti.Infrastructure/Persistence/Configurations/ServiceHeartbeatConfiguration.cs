using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Platform;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class ServiceHeartbeatConfiguration : IEntityTypeConfiguration<ServiceHeartbeat>
{
    public void Configure(EntityTypeBuilder<ServiceHeartbeat> builder)
    {
        builder.ToTable("service_heartbeats", Schemas.Platform);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("service_name").HasMaxLength(64);
        builder.Ignore(x => x.ServiceName);
        builder.Property(x => x.Instance).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(64).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.LastSeenAt).IsRequired();

        // What the service says about itself (the feed: a state word and its figures as JSON; ADR 0024). Null for services that only report that they are alive.
        builder.Property(x => x.State).HasMaxLength(ServiceHeartbeat.MaxStateLength);
        builder.Property(x => x.Details).HasColumnType("jsonb");
    }
}
