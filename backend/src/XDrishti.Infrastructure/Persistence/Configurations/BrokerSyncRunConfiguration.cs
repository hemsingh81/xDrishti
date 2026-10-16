using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.broker_sync_runs</c> (ADR 0025): one row per run of the broker history sync - trades or ledger, who started it, the dates it reads, how it ended and what it did (counts as JSON, no trade or ledger content). Never deleted: the
/// runtime role has no DELETE. At most one run per account is <c>running</c> (the database backs the advisory lock).
/// </summary>
internal sealed class BrokerSyncRunConfiguration : IEntityTypeConfiguration<BrokerSyncRun>
{
    public void Configure(EntityTypeBuilder<BrokerSyncRun> builder)
    {
        builder.ToTable("broker_sync_runs", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_broker_sync_runs_kind", "kind IN ('trades', 'ledger')");
            t.HasCheckConstraint("ck_broker_sync_runs_trigger", "trigger IN ('manual', 'scheduled', 'first')");
            t.HasCheckConstraint("ck_broker_sync_runs_status", "status IN ('running', 'succeeded', 'failed', 'skipped')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Kind).HasConversion(EnumConversions.Lowercase<BrokerSyncKind>()).HasMaxLength(6).IsRequired();
        builder.Property(x => x.Trigger).HasConversion(EnumConversions.Lowercase<BrokerSyncTrigger>()).HasMaxLength(9).IsRequired();
        builder.Property(x => x.FromDate).IsRequired();
        builder.Property(x => x.ToDate).IsRequired();
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<SyncStatus>()).HasMaxLength(10).IsRequired();
        DataJobConfiguration.JsonColumn(builder.Property(x => x.Counts));
        builder.Property(x => x.StartedBy).HasMaxLength(BrokerSyncRun.MaxStartedByLength).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.FinishedAt);
        builder.Property(x => x.Error).HasMaxLength(BrokerSyncRun.MaxErrorLength);
        builder.Ignore(x => x.IsRunning);

        builder.HasIndex(x => new { x.AccountId, x.Kind, x.StartedAt }).HasDatabaseName("ix_broker_sync_runs_account_kind_started");

        // At most one broker history run (of either kind) runs per account at any time.
        builder.HasIndex(x => x.AccountId, "ux_broker_sync_runs_one_running").HasDatabaseName("ux_broker_sync_runs_one_running").IsUnique().HasFilter("status = 'running'");
    }
}
