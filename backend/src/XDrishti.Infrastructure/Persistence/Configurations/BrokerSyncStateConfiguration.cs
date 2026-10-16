using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.broker_sync_state</c> (ADR 0025, 0027): per account and kind (<c>holdings</c>, <c>trades</c>, <c>ledger</c>, <c>quotes</c>), the last India date the broker history was read and stored up to (the watermark; null for a kind without one) and the
/// health of the sync - the days in a row whose last attempt failed, the latest failure, the failed attempts of that day, how far earlier history was read and the version of the Dhan mappers it was read with. The watermark only moves forward; every column is written with an
/// upsert or a guarded update, in the window's transaction where there is one. Never deleted from.
/// </summary>
internal sealed class BrokerSyncStateConfiguration : IEntityTypeConfiguration<BrokerSyncState>
{
    public void Configure(EntityTypeBuilder<BrokerSyncState> builder)
    {
        builder.ToTable("broker_sync_state", Schemas.Portfolio, t => t.HasCheckConstraint("ck_broker_sync_state_kind", "kind IN ('holdings', 'trades', 'ledger', 'quotes')"));
        builder.HasKey(x => new { x.AccountId, x.Kind });
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Kind).HasConversion(EnumConversions.Lowercase<SyncHealthKind>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.WatermarkDate);
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.ConsecutiveFailedDays).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.LastFailureReason).HasMaxLength(40);
        builder.Property(x => x.LastFailedAt);
        builder.Property(x => x.Attempts).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.DeepenedTo);
        builder.Property(x => x.DeepenExhausted).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.DeepenedOn);
        builder.Property(x => x.MapperVersion).IsRequired().HasDefaultValue(0);
    }
}
