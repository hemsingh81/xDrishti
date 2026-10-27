using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.hypotheses</c> (spec P6-10, ADR 0076): the hypothesis log, one row per state change; the newest row of a hypothesis is its current view. Insert-only for the runtime role. The foreign keys to <c>research.sim_runs (run_key)</c> and
/// <c>research.experiments (id)</c> are added by the migration in SQL.
/// </summary>
internal sealed class HypothesisRecordConfiguration : IEntityTypeConfiguration<HypothesisRecord>
{
    private const string States = "'proposed', 'approved', 'running', 'judged', 'accepted', 'rejected', 'expired'";

    public void Configure(EntityTypeBuilder<HypothesisRecord> builder)
    {
        builder.ToTable("hypotheses", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_hypotheses_state", $"state IN ({States}) AND (from_state IS NULL OR from_state IN ({States})) AND (state = 'proposed') = (from_state IS NULL) AND (state = 'proposed') = (hypothesis_id IS NULL) AND state IS DISTINCT FROM from_state");
            t.HasCheckConstraint("ck_hypotheses_shape", "source IN ('owner', 'grid', 'model') AND knob_kind IN ('Parameter', 'Exit', 'Slippage') AND baseline_run_key ~ '^[0-9a-f]{64}$' AND (candidate_run_key IS NULL OR (candidate_run_key ~ '^[0-9a-f]{64}$' AND candidate_run_key <> baseline_run_key))");
            t.HasCheckConstraint("ck_hypotheses_links", "(state NOT IN ('running', 'judged', 'accepted') OR candidate_run_key IS NOT NULL) AND (state NOT IN ('judged', 'accepted') OR experiment_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id).HasName("pk_hypotheses");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.State).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FromState).HasMaxLength(16);
        builder.Property(x => x.Source).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CellKey).HasMaxLength(400);
        builder.Property(x => x.KnobName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.KnobKind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.BaselineValue).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CandidateValue).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Rationale).HasMaxLength(500).IsRequired();
        builder.Property(x => x.BaselineRunKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.CandidateRunKey).HasColumnType("character(64)");
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<HypothesisRecord>().WithMany().HasForeignKey(x => x.HypothesisId).HasConstraintName("fk_hypotheses_hypotheses_hypothesis_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.HypothesisId, x.Id }).HasDatabaseName("ix_hypotheses_hypothesis_id_id");
        builder.HasIndex(x => x.ExperimentId).HasDatabaseName("ix_hypotheses_experiment_id");
    }
}
