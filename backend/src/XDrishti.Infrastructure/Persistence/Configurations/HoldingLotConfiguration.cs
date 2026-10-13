using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class HoldingLotConfiguration : IEntityTypeConfiguration<HoldingLot>
{
    public void Configure(EntityTypeBuilder<HoldingLot> builder)
    {
        builder.ToTable("holding_lots", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_holding_lots_source", "source IN ('import', 'added', 'manual', 'trade')");
            t.HasCheckConstraint("ck_holding_lots_qty", "qty > 0");
            t.HasCheckConstraint("ck_holding_lots_unit_cost", "unit_cost >= 0");
            // A lot is closed as a whole: day and price are both set or both missing.
            t.HasCheckConstraint("ck_holding_lots_closed", "(closed_on IS NULL) = (close_price IS NULL)");
            t.HasCheckConstraint("ck_holding_lots_closed_after_acquired", "closed_on IS NULL OR closed_on >= acquired_on");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.HoldingId).IsRequired();
        builder.Property(x => x.AcquiredOn).IsRequired();
        builder.Property(x => x.Qty).IsRequired();
        builder.Property(x => x.UnitCost).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Source).HasConversion(EnumConversions.Lowercase<LotSource>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.UsesDefaultDate).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ClosedOn);
        builder.Property(x => x.ClosePrice).HasPrecision(18, 4);
        builder.Property(x => x.Estimated).IsRequired();
        builder.Ignore(x => x.IsOpen);
        builder.HasIndex(x => x.HoldingId);
    }
}
