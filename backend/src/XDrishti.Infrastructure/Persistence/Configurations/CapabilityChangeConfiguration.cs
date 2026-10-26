using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Capabilities;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>platform.capability_changes</c> (spec P8-18 Part A, ADR 0128): every switch of a capability, appended and never edited. The newest id of a name decides; a name with no row is OFF. The names are a code whitelist, so there is no check on them: adding a capability is
/// not a migration. The shape is a check: an owner actor and a reason.
/// </summary>
internal sealed class CapabilityChangeConfiguration : IEntityTypeConfiguration<CapabilityChangeRecord>
{
    public void Configure(EntityTypeBuilder<CapabilityChangeRecord> builder)
    {
        builder.ToTable("capability_changes", Schemas.Platform, t => t.HasCheckConstraint(
            "ck_capability_changes_shape",
            "capability ~ '^[a-z][a-z0-9_]{0,39}$' AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_capability_changes");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Capability).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.Capability, x.Id }).HasDatabaseName("ix_capability_changes_capability_id");
    }
}
