using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.bar_gaps</c> (ADR 0017 §3): one row per instrument and session that had fewer 1-minute bars than the calendar expects, with what became of it. Written by
/// the Dhan phase (inserted and updated, never deleted: the runtime role has no DELETE) and read by the Data screen. No foreign key to the 40-million-row bars, one to the instrument list.
/// </summary>
internal sealed class BarGapConfiguration : IEntityTypeConfiguration<BarGap>
{
    public void Configure(EntityTypeBuilder<BarGap> builder)
    {
        builder.ToTable("bar_gaps", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_bar_gaps_status", "status IN ('open', 'filled', 'unfillable')");
            t.HasCheckConstraint("ck_bar_gaps_counts", "expected >= 0 AND found >= 0 AND attempts >= 0");
        });
        builder.HasKey(x => new { x.InstrumentId, x.Date });
        builder.Property(x => x.Date).HasColumnName("date");
        builder.Property(x => x.Status)
            .HasConversion(EnumConversions.Lowercase<GapStatus>())
            .HasMaxLength(10).IsRequired();
        builder.Property(x => x.DetectedAt).IsRequired();
        builder.HasOne<Market.InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);

        // "Which gaps are open" (the overview, the list) and "which instruments have open gaps" (the coverage filter).
        builder.HasIndex(x => new { x.Status, x.Date }).HasDatabaseName("ix_bar_gaps_status_date");
    }
}
