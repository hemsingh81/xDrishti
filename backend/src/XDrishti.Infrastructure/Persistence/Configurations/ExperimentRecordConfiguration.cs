using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.experiments</c> (spec P6-11, ADR 0072): the experiment ledger, one row per judged knob change, whatever the outcome. Its count over 30 days is the trials of the next experiment. Insert-only for the runtime role.
/// The foreign keys to <c>research.sim_runs (run_key)</c> are added by the migration in SQL (the unique index there is their target).
/// </summary>
internal sealed class ExperimentRecordConfiguration : IEntityTypeConfiguration<ExperimentRecord>
{
    public void Configure(EntityTypeBuilder<ExperimentRecord> builder)
    {
        builder.ToTable("experiments", Schemas.Research, t => t.HasCheckConstraint(
            "ck_experiments_shape",
            "experiment_key ~ '^[0-9a-f]{64}$' AND baseline_run_key ~ '^[0-9a-f]{64}$' AND candidate_run_key ~ '^[0-9a-f]{64}$' AND baseline_run_key <> candidate_run_key " +
            "AND knob_kind IN ('Parameter', 'Exit', 'Slippage') AND outcome IN ('Better', 'NotBetter', 'Inconclusive', 'CheaperAssumption') " +
            "AND trials >= 1 AND used_folds >= 0 AND folds_better >= 0 AND folds_better <= used_folds AND folds_needed >= 0 AND baseline_trades > 0 AND candidate_trades >= 0 AND candidate_trades <= baseline_trades AND compare_ms >= 0 " +
            "AND (outcome <> 'Better' OR used_folds >= 6)"));
        builder.HasKey(x => x.Id).HasName("pk_experiments");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.ExperimentKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.KnobName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.KnobKind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.BaselineValue).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CandidateValue).HasMaxLength(64).IsRequired();
        builder.Property(x => x.BaselineRunKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.CandidateRunKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.FromDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.ToDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.HoldOutStart).HasColumnType("date");
        builder.Property(x => x.Outcome).HasMaxLength(24).IsRequired();
        builder.Property(x => x.Reasons).HasColumnType("text").IsRequired();
        builder.Property(x => x.ReportText).HasColumnType("text").IsRequired();
        builder.Property(x => x.Parameters).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.ExperimentKey).IsUnique().HasDatabaseName("uq_experiments_experiment_key");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_experiments_created_at");
    }
}
