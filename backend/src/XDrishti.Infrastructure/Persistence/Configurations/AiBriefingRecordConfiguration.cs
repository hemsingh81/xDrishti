using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.briefings</c> (spec P9-06): the daily briefing and weekly review texts. Insert-only.</summary>
internal sealed class AiBriefingRecordConfiguration : IEntityTypeConfiguration<AiBriefingRecord>
{
    public void Configure(EntityTypeBuilder<AiBriefingRecord> builder)
    {
        builder.ToTable("briefings", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_briefings_kind", "kind IN ('daily', 'weekly')");
            t.HasCheckConstraint("ck_ai_briefings_source", "source IN ('model', 'deterministic')");
            t.HasCheckConstraint("ck_ai_briefings_reason", "reason IS NULL OR reason IN ('model_off', 'model_unavailable', 'guard_rejected', 'facts_thin')");
            t.HasCheckConstraint("ck_ai_briefings_model_source", "source <> 'model' OR model_id IS NOT NULL");
            t.HasCheckConstraint("ck_ai_briefings_reason_iff_deterministic", "(source = 'model') = (reason IS NULL)");
            t.HasCheckConstraint("ck_ai_briefings_weekly_footer", "kind <> 'weekly' OR text LIKE '%Nothing was applied%'");
            t.HasCheckConstraint("ck_ai_briefings_version", "version >= 1");
        });
        builder.HasKey(x => x.Id).HasName("pk_briefings");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(13).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(24);
        builder.Property(x => x.Text).HasColumnType("text").IsRequired();
        builder.Property(x => x.DeterministicText).HasColumnType("text").IsRequired();
        builder.Property(x => x.FactsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FactsHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(80);
        builder.Property(x => x.ModelSha256).HasColumnType("character(64)");
        builder.Property(x => x.PromptVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ParamsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BuiltBy).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.Kind, x.Subject, x.Version }).IsUnique().HasDatabaseName("ux_ai_briefings_kind_subject_version");
        builder.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("ix_ai_briefings_created_at");
    }
}
