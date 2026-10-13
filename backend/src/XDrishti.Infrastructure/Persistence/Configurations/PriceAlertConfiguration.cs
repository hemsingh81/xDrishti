using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>portfolio.price_alerts</c>: one-shot price alerts on master-list instruments (ADR 0022). Never deleted: the runtime role has no DELETE; the status changes by conditional updates.</summary>
internal sealed class PriceAlertConfiguration : IEntityTypeConfiguration<PriceAlert>
{
    public void Configure(EntityTypeBuilder<PriceAlert> builder)
    {
        builder.ToTable("price_alerts", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_price_alerts_direction", "direction IN ('at_or_below', 'at_or_above')");
            t.HasCheckConstraint("ck_price_alerts_status", "status IN ('active', 'triggered', 'cancelled')");
            t.HasCheckConstraint("ck_price_alerts_level", "level > 0 AND level <= 10000000");

            // A triggered alert says at what price and when.
            t.HasCheckConstraint("ck_price_alerts_triggered", "status <> 'triggered' OR (triggered_at IS NOT NULL AND trigger_price IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // A reference by id into the instrument master (another module): no foreign key, the master is refreshed from outside and may retire rows.
        builder.Property(x => x.InstrumentId).IsRequired();
        builder.Property(x => x.Direction)
            .HasConversion(new ValueConverter<AlertDirection, string>(v => v.ToCode(), v => ParseDirection(v)))
            .HasMaxLength(12)
            .IsRequired();
        builder.Property(x => x.Level).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(PriceAlert.MaxNoteLength);
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<AlertStatus>()).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(PriceAlert.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.TriggeredAt);
        builder.Property(x => x.TriggerPrice).HasPrecision(18, 4);

        // The quote poll asks for the active alerts every tick; the list filters by status.
        builder.HasIndex(x => new { x.Status, x.InstrumentId }).HasDatabaseName("ix_price_alerts_status_instrument");
    }

    private static AlertDirection ParseDirection(string code) =>
        AlertDirections.TryParse(code, out var direction) ? direction : throw new InvalidOperationException("Unknown alert direction in the database.");
}
