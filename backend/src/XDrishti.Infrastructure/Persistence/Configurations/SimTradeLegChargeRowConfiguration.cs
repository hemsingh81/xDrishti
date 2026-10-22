using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.sim_trade_leg_charges</c> (spec P4-05, D9): the charges of each fill of a costed trade with legs, keyed by the trade and the place in the costing's list. Insert-only for the runtime role; a foreign key to the trade without
/// cascade. The amounts are <c>numeric</c> without a scale so that a decimal round-trips with its value, as in <c>sim_trades</c>.
/// </summary>
internal sealed class SimTradeLegChargeRowConfiguration : IEntityTypeConfiguration<SimTradeLegChargeRow>
{
    public void Configure(EntityTypeBuilder<SimTradeLegChargeRow> builder)
    {
        builder.ToTable("sim_trade_leg_charges", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_sim_trade_leg_charges_ordinals", "ordinal >= 0 AND charge_ordinal >= 0");
            t.HasCheckConstraint("ck_sim_trade_leg_charges_quantity", "quantity >= 0");
        });
        builder.HasKey(x => new { x.RunId, x.OpenAtEnd, x.Ordinal, x.ChargeOrdinal }).HasName("pk_sim_trade_leg_charges");
        builder.Property(x => x.EpochSeconds).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        foreach (var name in new[] { nameof(SimTradeLegChargeRow.Turnover), nameof(SimTradeLegChargeRow.Brokerage), nameof(SimTradeLegChargeRow.Stt), nameof(SimTradeLegChargeRow.ExchangeTxn), nameof(SimTradeLegChargeRow.SebiFee), nameof(SimTradeLegChargeRow.StampDuty), nameof(SimTradeLegChargeRow.OtherCharges), nameof(SimTradeLegChargeRow.DpCharge), nameof(SimTradeLegChargeRow.Gst) })
        {
            builder.Property<decimal>(name).HasColumnType("numeric").IsRequired();
        }

        builder.HasOne<SimTradeRow>().WithMany().HasForeignKey(x => new { x.RunId, x.OpenAtEnd, x.Ordinal }).HasConstraintName("fk_sim_trade_leg_charges_trade").OnDelete(DeleteBehavior.Restrict);
    }
}
