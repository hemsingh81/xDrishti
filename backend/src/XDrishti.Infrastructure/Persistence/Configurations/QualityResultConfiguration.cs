using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.quality_results</c> (ADR 0017 §4): one row per run and instrument — status, score, the bars checked and missing minutes of the window, and the checks as <c>jsonb</c> (the long lists under <c>details</c>).
/// Inserted by the quality phase, never deleted. An instrument's current result is the one of the latest run that checked it.
/// </summary>
internal sealed class QualityResultConfiguration : IEntityTypeConfiguration<QualityResult>
{
    public void Configure(EntityTypeBuilder<QualityResult> builder)
    {
        builder.ToTable("quality_results", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_quality_results_status", "status IN ('pass', 'warning', 'fail')");
            t.HasCheckConstraint("ck_quality_results_score", "score BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_quality_results_counts", "bars_checked >= 0 AND missing_minutes >= 0");
        });
        builder.HasKey(x => new { x.RunId, x.InstrumentId });
        builder.Property(x => x.Status).HasMaxLength(7).IsRequired();
        builder.Property(x => x.Score).HasColumnType("smallint");
        DataJobConfiguration.JsonColumn(builder.Property(x => x.Checks));
        builder.HasOne<QualityRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Market.InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);

        // "The latest result of an instrument".
        builder.HasIndex(x => new { x.InstrumentId, x.RunId }).HasDatabaseName("ix_quality_results_instrument_run");
    }
}
