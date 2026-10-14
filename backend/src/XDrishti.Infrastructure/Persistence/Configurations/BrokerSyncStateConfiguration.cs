using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.broker_sync_state</c> (ADR 0025): per account and kind, the last India date the broker history was read and stored up to (the watermark). Only moves forward; written with an upsert, in the window's transaction.
/// </summary>
internal sealed class BrokerSyncStateConfiguration : IEntityTypeConfiguration<BrokerSyncState>
{
    public void Configure(EntityTypeBuilder<BrokerSyncState> builder)
    {
        builder.ToTable("broker_sync_state", Schemas.Portfolio, t => t.HasCheckConstraint("ck_broker_sync_state_kind", "kind IN ('trades', 'ledger')"));
        builder.HasKey(x => new { x.AccountId, x.Kind });
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Kind).HasConversion(EnumConversions.Lowercase<BrokerSyncKind>()).HasMaxLength(6).IsRequired();
        builder.Property(x => x.WatermarkDate).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
