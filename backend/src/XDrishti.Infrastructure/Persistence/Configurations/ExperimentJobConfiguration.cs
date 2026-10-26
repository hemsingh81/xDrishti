using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.experiment_jobs</c> (spec P8-18 Part B, ADR 0129): the replay jobs the owner started. The table is the queue and the daily-limit counter. One job at a time is the partial unique index on the active states. The foreign keys to the stored runs and to the experiment it produced, the partial unique index, the grants and the triggers are in the migration (SQL).
/// </summary>
internal sealed class ExperimentJobConfiguration : IEntityTypeConfiguration<ExperimentJobRecord>
{
    public void Configure(EntityTypeBuilder<ExperimentJobRecord> builder)
    {
        builder.ToTable("experiment_jobs", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_experiment_jobs_shape", "job_key ~ '^[0-9a-f]{64}$' AND baseline_run_key ~ '^[0-9a-f]{64}$' AND knob_kind IN ('exit', 'slippage') AND created_by LIKE 'owner:%' AND length(btrim(created_by)) > 6 AND length(btrim(reason)) > 0 AND hold_out_months BETWEEN 0 AND 120 AND instruments >= 1");
            t.HasCheckConstraint("ck_experiment_jobs_state", "state IN ('queued', 'running', 'succeeded', 'failed', 'cancelled', 'interrupted')");
            t.HasCheckConstraint("ck_experiment_jobs_progress", "progress_done >= 0 AND progress_total >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_experiment_jobs");
        builder.Property(x => x.JobKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.BaselineRunKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.KnobKind).HasMaxLength(10).IsRequired();
        builder.Property(x => x.KnobName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.BaselineValue).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CandidateValue).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.State).HasMaxLength(12).IsRequired();
        builder.Property(x => x.Stage).HasMaxLength(20);
        builder.Property(x => x.ProgressDone).HasDefaultValue(0);
        builder.Property(x => x.ProgressTotal).HasDefaultValue(0);
        builder.Property(x => x.CancelRequested).HasDefaultValue(false);
        builder.Property(x => x.OtherHeavyJobsSeen).HasDefaultValue(false);
        builder.Property(x => x.CandidateRunKey).HasColumnType("character(64)");
        builder.Property(x => x.ErrorCode).HasMaxLength(80);
        builder.Property(x => x.ErrorText).HasMaxLength(500);
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Host).HasMaxLength(40);
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_experiment_jobs_created_at");
        builder.HasIndex(x => new { x.JobKey, x.CreatedAt }).HasDatabaseName("ix_experiment_jobs_job_key");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_experiment_jobs_queued").HasFilter("state = 'queued'");
    }
}
