using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.SystemBaskets;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class FoLotSizeRecordConfiguration : IEntityTypeConfiguration<FoLotSizeRecord>
{
    public void Configure(EntityTypeBuilder<FoLotSizeRecord> builder)
    {
        builder.ToTable("fo_lot_sizes", Schemas.Market, t => t.HasCheckConstraint("ck_fo_lot_sizes_lot_size", "lot_size > 0"));
        builder.HasKey(x => new { x.InstrumentId, x.EffectiveFrom });
        builder.Property(x => x.ExpiryLabel).HasMaxLength(10).IsRequired();
        builder.HasOne<InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BasketFetchRecord>().WithMany().HasForeignKey(x => x.FetchId).OnDelete(DeleteBehavior.Restrict);
    }
}
