using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Strength;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>market.relative_strength_daily</c> (BK-09): one row per day, share, basket and definition; insert-only for the runtime role.</summary>
internal sealed class RelativeStrengthConfiguration : IEntityTypeConfiguration<RelativeStrengthRecord>
{
    public void Configure(EntityTypeBuilder<RelativeStrengthRecord> builder)
    {
        builder.ToTable("relative_strength_daily", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_relative_strength_daily_state", "state IN ('value', 'insufficient', 'not_evaluable', 'unavailable', 'short_history', 'corporate_action_unresolved')");
            t.HasCheckConstraint("ck_relative_strength_daily_basis", "membership_basis IN ('dated', 'single_snapshot', 'none')");
            t.HasCheckConstraint("ck_relative_strength_daily_rank", "pct_rank IS NULL OR (pct_rank >= 0 AND pct_rank <= 1)");
        });
        builder.HasKey(x => new { x.AsOf, x.InstrumentId, x.BasketId, x.Lookback, x.Skip });
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
        builder.Property(x => x.MembershipBasis).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.BasketId, x.AsOf }).HasDatabaseName("ix_relative_strength_daily_basket_id_as_of");
        builder.HasIndex(x => new { x.InstrumentId, x.AsOf }).HasDatabaseName("ix_relative_strength_daily_instrument_id_as_of");
        builder.HasOne<InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>market.sector_strength_daily</c> (BK-09): one row per day, sector and window; insert-only for the runtime role.</summary>
internal sealed class SectorStrengthConfiguration : IEntityTypeConfiguration<SectorStrengthRecord>
{
    public void Configure(EntityTypeBuilder<SectorStrengthRecord> builder)
    {
        builder.ToTable("sector_strength_daily", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_sector_strength_daily_state", "state IN ('value', 'insufficient', 'not_evaluable', 'unavailable', 'short_history', 'corporate_action_unresolved')");
            t.HasCheckConstraint("ck_sector_strength_daily_rank", "rank IS NULL OR rank >= 1");
        });
        builder.HasKey(x => new { x.AsOf, x.Sector, x.WindowSessions });
        builder.Property(x => x.Sector).HasMaxLength(128).IsRequired();
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
    }
}
