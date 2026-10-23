using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.model_versions</c> (spec P6-07, ADR 0070): one row per registration of a trained model (the file is <c>data/models/&lt;sha256&gt;</c>), with the training-set id, the validation verdict and what made it. Insert-only for the runtime role.
/// The foreign key to <c>research.training_sets (set_id)</c> is added by the migration in SQL (the unique index there is its target).
/// </summary>
internal sealed class ModelVersionRecordConfiguration : IEntityTypeConfiguration<ModelVersionRecord>
{
    public void Configure(EntityTypeBuilder<ModelVersionRecord> builder)
    {
        builder.ToTable("model_versions", Schemas.Research, t => t.HasCheckConstraint(
            "ck_model_versions_shape",
            "sha256 ~ '^[0-9a-f]{64}$' AND version_key ~ '^[0-9a-f]{64}$' AND training_set_id ~ '^[0-9a-f]{64}$' AND verdict IN ('Eligible', 'CannotBeatBaselines', 'Inconclusive') AND artifact_bytes > 0 AND feature_width > 0 " +
            "AND cardinality(calibration_scores) > 0 AND cardinality(calibration_scores) = cardinality(calibration_values) AND cardinality(importance) = feature_width AND train_rows > 0 AND fit_rows >= 0 AND calibration_rows >= 0 AND used_folds >= 0"));
        builder.HasKey(x => x.Id).HasName("pk_model_versions");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.VersionKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Sha256).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Trainer).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Note).HasColumnType("text");
        builder.Property(x => x.TrainingSetId).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.FeatureVersion).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FromDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.ToDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.HoldOutStart).HasColumnType("date");
        builder.Property(x => x.Verdict).HasMaxLength(24).IsRequired();
        builder.Property(x => x.Reasons).HasColumnType("text").IsRequired();
        builder.Property(x => x.ReportText).HasColumnType("text").IsRequired();
        builder.Property(x => x.Parameters).HasColumnType("text").IsRequired();
        builder.Property(x => x.CalibrationScores).HasColumnType("double precision[]").IsRequired();
        builder.Property(x => x.CalibrationValues).HasColumnType("double precision[]").IsRequired();
        builder.Property(x => x.Importance).HasColumnType("double precision[]").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.VersionKey).IsUnique().HasDatabaseName("uq_model_versions_version_key");
        builder.HasIndex(x => x.Sha256).HasDatabaseName("ix_model_versions_sha256");
        builder.HasIndex(x => x.TrainingSetId).HasDatabaseName("ix_model_versions_training_set_id");
    }
}
