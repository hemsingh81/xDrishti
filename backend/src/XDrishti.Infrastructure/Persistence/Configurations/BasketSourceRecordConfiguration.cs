using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Market.SystemBaskets;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class BasketSourceRecordConfiguration : IEntityTypeConfiguration<BasketSourceRecord>
{
    public void Configure(EntityTypeBuilder<BasketSourceRecord> builder)
    {
        builder.ToTable("basket_sources", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_basket_sources_state", "state IN ('ok', 'stale', 'rejected', 'failed', 'never')");
            t.HasCheckConstraint("ck_basket_sources_bounds", "expected_min >= 0 AND expected_max >= expected_min");
        });
        builder.HasKey(x => x.BasketId);
        builder.Property(x => x.BasketId).ValueGeneratedNever();
        builder.Property(x => x.SourceUrl).HasMaxLength(300).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.HostAlt).HasMaxLength(300);
        builder.Property(x => x.FormatVersion).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Cadence).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Enabled).HasDefaultValue(false);
        builder.Property(x => x.LicenseNote).HasMaxLength(500);
        builder.Property(x => x.State).HasMaxLength(10).HasDefaultValue(BasketSourceStates.Never).IsRequired();
        builder.Property(x => x.Etag).HasMaxLength(200);
        builder.Property(x => x.LastModified).HasMaxLength(100);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.FailCount).HasDefaultValue(0);
        builder.Property(x => x.KnownThrough).HasColumnType("date");
        builder.HasOne<Basket>().WithOne().HasForeignKey<BasketSourceRecord>(x => x.BasketId).OnDelete(DeleteBehavior.Restrict);
    }
}
