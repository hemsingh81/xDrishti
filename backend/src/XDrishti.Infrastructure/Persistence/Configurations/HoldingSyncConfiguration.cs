using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class HoldingSyncConfiguration : IEntityTypeConfiguration<HoldingSync>
{
    public void Configure(EntityTypeBuilder<HoldingSync> builder)
    {
        builder.ToTable("holding_syncs", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_holding_syncs_trigger", "trigger IN ('manual', 'auto', 'import')");
            t.HasCheckConstraint("ck_holding_syncs_status", "status IN ('running', 'succeeded', 'failed', 'skipped')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Trigger).HasConversion(EnumConversions.Lowercase<SyncTrigger>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.RequestedBy).HasMaxLength(HoldingSync.MaxRequestedByLength).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.FinishedAt);
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<SyncStatus>()).HasMaxLength(10).IsRequired();
        builder.Property(x => x.NewCount).IsRequired();
        builder.Property(x => x.IncreasedCount).IsRequired();
        builder.Property(x => x.DecreasedCount).IsRequired();
        builder.Property(x => x.ClosedCount).IsRequired();
        builder.Property(x => x.UnchangedCount).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(HoldingSync.MaxErrorLength);
        builder.Ignore(x => x.IsRunning);
        builder.Ignore(x => x.Counts);
        builder.HasIndex(x => new { x.AccountId, x.StartedAt }).HasDatabaseName("ix_holding_syncs_account_started");

        // The automatic refresh asks "what did this account's automatic runs do last" and "was a run skipped today" every minute: the index answers from itself.
        builder.HasIndex(x => new { x.AccountId, x.Trigger, x.StartedAt }, "ix_holding_syncs_account_trigger_started").HasDatabaseName("ix_holding_syncs_account_trigger_started").IncludeProperties(x => x.Status);

        // At most one sync runs per account at any time (the database backs the advisory lock).
        builder.HasIndex(x => x.AccountId, "ux_holding_syncs_one_running").HasDatabaseName("ux_holding_syncs_one_running").IsUnique().HasFilter("status = 'running'");
    }
}
