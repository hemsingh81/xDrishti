using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>research.learning_reports</c> (spec P8-12, ADR 0110): generic learning reports keyed by kind, insert-only for the runtime role. The database refuses a text without its limits, a bad kind, a bad hash or an empty builder.</summary>
internal sealed class LearningReportRecordConfiguration : IEntityTypeConfiguration<LearningReportRecord>
{
    public void Configure(EntityTypeBuilder<LearningReportRecord> builder)
    {
        builder.ToTable("learning_reports", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_learning_reports_shape", "kind ~ '^[a-z][a-z_]{1,38}$' AND input_hash ~ '^[0-9a-f]{64}$' AND length(outcome) > 0 AND basis IN ('gross','costed_unconfirmed','net_confirmed') AND position('limit:' IN text) > 0 AND length(built_by) > 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_learning_reports");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        builder.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Basis).HasMaxLength(24).IsRequired();
        builder.Property(x => x.SourcesJson).IsRequired();
        builder.Property(x => x.OptionsJson).IsRequired();
        builder.Property(x => x.ReportJson).IsRequired();
        builder.Property(x => x.Text).IsRequired();
        builder.Property(x => x.BuiltBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.Kind, x.InputHash }).IsUnique().HasDatabaseName("uq_learning_reports_kind_hash"); // the same inputs are stored once
        builder.HasIndex(x => new { x.Kind, x.CreatedAt, x.Id }).IsDescending(false, true, true).HasDatabaseName("ix_learning_reports_kind_created");
    }
}
