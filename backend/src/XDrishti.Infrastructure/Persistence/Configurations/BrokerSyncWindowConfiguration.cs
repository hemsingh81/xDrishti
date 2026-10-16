using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.broker_sync_windows</c> (ADR 0027): the date windows of an account's trade or ledger read that dropped rows for a data reason and have not been read again since (<c>needs_reread</c>). A window that was read cleanly leaves no row; a row is cleared
/// (updated, never deleted) when a later read covers its dates. The runtime role has no DELETE.
/// </summary>
internal sealed class BrokerSyncWindowConfiguration : IEntityTypeConfiguration<BrokerSyncWindow>
{
    public void Configure(EntityTypeBuilder<BrokerSyncWindow> builder)
    {
        builder.ToTable("broker_sync_windows", Schemas.Portfolio, t => t.HasCheckConstraint("ck_broker_sync_windows_kind", "kind IN ('trades', 'ledger')"));
        builder.HasKey(x => new { x.AccountId, x.Kind, x.FromDate });
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Kind).HasConversion(EnumConversions.Lowercase<SyncHealthKind>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.FromDate).IsRequired();
        builder.Property(x => x.ToDate).IsRequired();
        builder.Property(x => x.Dropped).IsRequired();
        builder.Property(x => x.NeedsReread).IsRequired();
        builder.Property(x => x.NotedAt).IsRequired();

        builder.HasIndex(x => new { x.AccountId, x.Kind, x.FromDate }, "ix_broker_sync_windows_reread").HasDatabaseName("ix_broker_sync_windows_reread").HasFilter("needs_reread");
    }
}
