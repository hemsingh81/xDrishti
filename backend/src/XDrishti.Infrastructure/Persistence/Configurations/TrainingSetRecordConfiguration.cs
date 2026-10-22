using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.training_sets</c> (spec P6-04, ADR 0066; moved here from P6-03): the record of a training set, one row per set id. Insert-only for the runtime role. The examples are not stored: they are rebuilt from the stored runs and features and the id proves
/// they are the same.
/// </summary>
internal sealed class TrainingSetRecordConfiguration : IEntityTypeConfiguration<TrainingSetRecord>
{
    public void Configure(EntityTypeBuilder<TrainingSetRecord> builder)
    {
        builder.ToTable("training_sets", Schemas.Research, t => t.HasCheckConstraint(
            "ck_training_sets_shape",
            "set_id ~ '^[0-9a-f]{64}$' AND label_set_id ~ '^[0-9a-f]{64}$' AND row_count > 0 AND positives >= 0 AND negatives >= 0 AND positives + negatives = row_count AND labelled >= row_count AND unresolved >= 0 AND no_target >= 0 AND dropped_no_features >= 0 AND dropped_unknown >= 0"));
        builder.HasKey(x => x.Id).HasName("pk_training_sets");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.SetId).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.LabelSetId).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.RuleVersion).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FeatureVersion).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Basis).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Sources).HasColumnType("text").IsRequired();
        builder.Property(x => x.RequiredFeatures).HasColumnType("text").IsRequired();
        builder.Property(x => x.FromDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.ToDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.SetId).IsUnique().HasDatabaseName("uq_training_sets_set_id");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_training_sets_created_at");
    }
}
