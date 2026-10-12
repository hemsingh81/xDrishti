using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.quality_runs</c> (ADR 0017 §4): one row per run of the data quality checks — what started it, the window, the counts by result, the mean score and the end. Inserted and updated by the quality
/// phase, never deleted (the runtime role has no DELETE). At most one run is <c>running</c> (a unique partial index backs it). No foreign key to the data job (jobs and runs are both kept for good); one to the instrument.
/// </summary>
internal sealed class QualityRunConfiguration : IEntityTypeConfiguration<QualityRun>
{
    public void Configure(EntityTypeBuilder<QualityRun> builder)
    {
        builder.ToTable("quality_runs", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_quality_runs_trigger", "trigger IN ('manual', 'daily', 'after_job')");
            t.HasCheckConstraint("ck_quality_runs_status", "status IN ('running', 'succeeded', 'failed')");
            t.HasCheckConstraint("ck_quality_runs_counts", "sessions >= 0 AND instruments >= 0 AND passed >= 0 AND warned >= 0 AND failed >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Trigger).HasConversion(new QualityTriggerConverter()).HasMaxLength(9).IsRequired();
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<QualityRunStatus>()).HasMaxLength(9).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(QualityRun.MaxErrorLength);
        builder.HasOne<Market.InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);

        // At most one run is in progress (the quality phase runs inside a data job, which is one at a time; this is the second line).
        builder.HasIndex(x => x.Status, "ux_quality_runs_one_running").HasDatabaseName("ux_quality_runs_one_running").IsUnique().HasFilter("status = 'running'");

        // "Newest first" (the history) and "the latest run over every instrument" (the overview and the gate).
        builder.HasIndex(x => x.StartedAt).HasDatabaseName("ix_quality_runs_started_at");
    }

    private sealed class QualityTriggerConverter()
        : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<QualityTrigger, string>(v => v.ToCode(), v => QualityTriggerCodes.FromCode(v));
}
