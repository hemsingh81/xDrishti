using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.lessons</c> (spec P6-16, ADR 0078): the lesson log, one row per state change; the newest row of a lesson is its current view. Insert-only for the runtime role. The foreign keys to <c>research.hypotheses (id)</c> and
/// <c>research.experiments (id)</c> and the partial unique index on the opening rows are added by the migration in SQL.
/// </summary>
internal sealed class LessonRecordConfiguration : IEntityTypeConfiguration<LessonRecord>
{
    private const string States = "'open', 'linked', 'closed', 'dismissed'";

    public void Configure(EntityTypeBuilder<LessonRecord> builder)
    {
        builder.ToTable("lessons", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_lessons_state", $"state IN ({States}) AND (from_state IS NULL OR from_state IN ({States})) AND (state = 'open') = (from_state IS NULL) AND (state = 'open') = (lesson_id IS NULL) AND state IS DISTINCT FROM from_state");
            t.HasCheckConstraint("ck_lessons_shape", "monitor_kind IN ('growing_tag', 'rolling_band', 'cusum', 'calibration')");
            t.HasCheckConstraint("ck_lessons_links", "(state <> 'open' OR (hypothesis_ref IS NULL AND experiment_id IS NULL)) AND (state <> 'linked' OR (hypothesis_ref IS NOT NULL) <> (experiment_id IS NOT NULL)) AND (state <> 'closed' OR experiment_id IS NOT NULL)");
            t.HasCheckConstraint("ck_lessons_result", "(state = 'closed') = (result IS NOT NULL) AND (result IS NULL OR result IN ('improved', 'no_measurable_change')) AND (state = 'closed' OR (experiment_outcome IS NULL AND before_total_r IS NULL AND after_total_r IS NULL AND before_mean_r IS NULL AND after_mean_r IS NULL))");
            t.HasCheckConstraint("ck_lessons_closed", "state <> 'closed' OR (experiment_outcome IS NOT NULL AND before_total_r IS NOT NULL AND after_total_r IS NOT NULL AND (result = 'improved') = (experiment_outcome = 'Better'))");
        });
        builder.HasKey(x => x.Id).HasName("pk_lessons");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.State).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FromState).HasMaxLength(16);
        builder.Property(x => x.MonitorKind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(400).IsRequired();
        builder.Property(x => x.EvidenceKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Problem).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Result).HasMaxLength(24);
        builder.Property(x => x.ExperimentOutcome).HasMaxLength(24);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<LessonRecord>().WithMany().HasForeignKey(x => x.LessonId).HasConstraintName("fk_lessons_lessons_lesson_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LessonId, x.Id }).HasDatabaseName("ix_lessons_lesson_id_id");
    }
}
