using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.sim_trade_legs</c> (spec P4-05, D9): one row per leg of a stored trade, keyed by the trade it belongs to and its place in the trade's list of legs. Insert-only for the runtime role, like its parent; a foreign key without cascade (nothing
/// is deleted by the runtime role). The kind is held to the closed set of <see cref="LegKind"/>.
/// </summary>
internal sealed class SimTradeLegRowConfiguration : IEntityTypeConfiguration<SimTradeLegRow>
{
    public void Configure(EntityTypeBuilder<SimTradeLegRow> builder)
    {
        var kinds = string.Join(", ", EnumText<LegKind>.Names.Select(n => "'" + n + "'"));
        builder.ToTable("sim_trade_legs", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_sim_trade_legs_ordinals", "ordinal >= 0 AND leg_ordinal >= 0");
            t.HasCheckConstraint("ck_sim_trade_legs_kind", $"kind IN ({kinds})");
            t.HasCheckConstraint("ck_sim_trade_legs_share", "share > 0");
        });
        builder.HasKey(x => new { x.RunId, x.OpenAtEnd, x.Ordinal, x.LegOrdinal }).HasName("pk_sim_trade_legs");
        builder.Property(x => x.EpochSeconds).IsRequired();
        builder.Property(x => x.Price).IsRequired();
        builder.Property(x => x.FilledPrice).IsRequired();
        builder.Property(x => x.Share).HasColumnType("numeric").IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(64).IsRequired();
        builder.HasOne<SimTradeRow>().WithMany().HasForeignKey(x => new { x.RunId, x.OpenAtEnd, x.Ordinal }).HasConstraintName("fk_sim_trade_legs_trade").OnDelete(DeleteBehavior.Restrict);
    }
}
