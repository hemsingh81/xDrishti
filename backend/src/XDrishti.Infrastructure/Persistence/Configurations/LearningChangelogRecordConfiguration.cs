using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>research.learning_changelog</c> (spec P6-08, ADR 0073): what changed, by whom or what, why, before and after, and the evidence. Insert-only for the runtime role; a revert is a new row.</summary>
internal sealed class LearningChangelogRecordConfiguration : IEntityTypeConfiguration<LearningChangelogRecord>
{
    private const string CellStates = "'research', 'candidate', 'paper', 'active', 'paused', 'retired'";
    private const string ModelRoles = "'challenger', 'champion', 'retired'";

    public void Configure(EntityTypeBuilder<LearningChangelogRecord> builder)
    {
        builder.ToTable("learning_changelog", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_learning_changelog_kind", "kind IN ('transition', 'designation', 'revert') AND subject_kind IN ('cell', 'model')");
            t.HasCheckConstraint("ck_learning_changelog_revert", "(kind = 'revert') = (reverts_id IS NOT NULL) AND (kind <> 'revert' OR subject_kind = 'cell')");
            t.HasCheckConstraint("ck_learning_changelog_states", $"from_state <> to_state AND (subject_kind <> 'cell' OR (from_state IN ({CellStates}) AND to_state IN ({CellStates}))) AND (subject_kind <> 'model' OR (from_state IN ({ModelRoles}) AND to_state IN ({ModelRoles})))");
            t.HasCheckConstraint("ck_learning_changelog_evidence", "cardinality(evidence_ids) <= 20 AND (paper_days IS NULL OR paper_days >= 0)");
        });
        builder.HasKey(x => x.Id).HasName("pk_learning_changelog");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SubjectKind).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        builder.Property(x => x.FromState).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ToState).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EvidenceIds).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.BeforeRef).HasMaxLength(200);
        builder.Property(x => x.AfterRef).HasMaxLength(200);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<LearningChangelogRecord>().WithMany().HasForeignKey(x => x.RevertsId).HasConstraintName("fk_learning_changelog_learning_changelog_reverts_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.RevertsId).IsUnique().HasFilter("reverts_id IS NOT NULL").HasDatabaseName("uq_learning_changelog_reverts_id");
        builder.HasIndex(x => new { x.Subject, x.Id }).HasDatabaseName("ix_learning_changelog_subject_id");
    }
}
