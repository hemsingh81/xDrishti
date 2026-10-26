using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Paper;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>paper.config_snapshots</c> (spec P10-05, ADR 0119): the fingerprinted configuration at each capture run. Insert-only for the runtime role.</summary>
internal sealed class ConfigSnapshotRecordConfiguration : IEntityTypeConfiguration<ConfigSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<ConfigSnapshotRecord> builder)
    {
        builder.ToTable("config_snapshots", Schemas.Paper, t => t.HasCheckConstraint("ck_config_snapshots_shape", "length(hash) = 64 AND length(btrim(process)) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_config_snapshots");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Hash).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.Components).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Process).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.SessionDate, x.Hash }).IsUnique().HasDatabaseName("uq_config_snapshots_session_hash");
        builder.HasIndex(x => x.TakenAt).HasDatabaseName("ix_config_snapshots_taken_at");
    }
}

/// <summary><c>paper.config_changes</c>: each difference between two snapshots, classed. Insert-only.</summary>
internal sealed class ConfigChangeRecordConfiguration : IEntityTypeConfiguration<ConfigChangeRecord>
{
    public void Configure(EntityTypeBuilder<ConfigChangeRecord> builder)
    {
        builder.ToTable("config_changes", Schemas.Paper, t => t.HasCheckConstraint("ck_config_changes_shape", "class IN ('frozen', 'risk_down', 'free') AND effect IN ('new_epoch', 'noted')"));
        builder.HasKey(x => x.Id).HasName("pk_config_changes");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Key).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Class).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Effect).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ExplainedBy).HasMaxLength(40);
        builder.HasIndex(x => x.EpochId).HasDatabaseName("ix_config_changes_epoch_id");
        builder.HasOne<ConfigSnapshotRecord>().WithMany().HasForeignKey(x => x.FromSnapshotId).HasConstraintName("fk_config_changes_config_snapshots_from_snapshot_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigSnapshotRecord>().WithMany().HasForeignKey(x => x.ToSnapshotId).HasConstraintName("fk_config_changes_config_snapshots_to_snapshot_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaperEpochRecord>().WithMany().HasForeignKey(x => x.EpochId).HasConstraintName("fk_config_changes_epochs_epoch_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.epoch_notes</c>: the owner's notes on epochs and changes. Insert-only.</summary>
internal sealed class EpochNoteRecordConfiguration : IEntityTypeConfiguration<EpochNoteRecord>
{
    public void Configure(EntityTypeBuilder<EpochNoteRecord> builder)
    {
        builder.ToTable("epoch_notes", Schemas.Paper, t => t.HasCheckConstraint("ck_epoch_notes_shape", "length(btrim(text)) > 0 AND length(btrim(actor)) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_epoch_notes");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Text).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.EpochId).HasDatabaseName("ix_epoch_notes_epoch_id");
        builder.HasOne<PaperEpochRecord>().WithMany().HasForeignKey(x => x.EpochId).HasConstraintName("fk_epoch_notes_epochs_epoch_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigChangeRecord>().WithMany().HasForeignKey(x => x.ChangeId).HasConstraintName("fk_epoch_notes_config_changes_change_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.weekly_reviews</c> (spec P10-03, ADR 0121): the weekly paper review, one row per ISO week and epoch, insert-only for the runtime role. The database refuses a text that does not open with the pinned line.</summary>
internal sealed class PaperReviewRecordConfiguration : IEntityTypeConfiguration<PaperReviewRecord>
{
    public void Configure(EntityTypeBuilder<PaperReviewRecord> builder)
    {
        builder.ToTable("weekly_reviews", Schemas.Paper, t => t.HasCheckConstraint("ck_weekly_reviews_shape", "iso_week ~ '^[0-9]{4}-W[0-9]{2}$' AND text LIKE 'Paper only: no order was placed and none exists.%' AND length(btrim(built_by)) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_weekly_reviews");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.IsoWeek).HasColumnType("text").IsRequired();
        builder.Property(x => x.BuiltBy).HasColumnType("text").IsRequired();
        builder.Property(x => x.Text).HasColumnType("text").IsRequired();
        builder.Property(x => x.Sections).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.IsoWeek, x.EpochId }).IsUnique().HasDatabaseName("uq_weekly_reviews_week_epoch"); // one review per week and epoch; the second insert is skipped
        builder.HasOne<PaperEpochRecord>().WithMany().HasForeignKey(x => x.EpochId).HasConstraintName("fk_weekly_reviews_epochs_epoch_id").OnDelete(DeleteBehavior.Restrict);
    }
}
