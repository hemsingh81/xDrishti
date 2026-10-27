using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.strategy_drafts</c> (spec P9-07): finished drafting jobs. Insert-only.</summary>
internal sealed class AiStrategyDraftRecordConfiguration : IEntityTypeConfiguration<AiStrategyDraftRecord>
{
    public void Configure(EntityTypeBuilder<AiStrategyDraftRecord> builder)
    {
        builder.ToTable("strategy_drafts", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_strategy_drafts_status", "status IN ('valid', 'invalid', 'declined', 'failed')");
            t.HasCheckConstraint("ck_ai_strategy_drafts_via", "via IN ('job', 'chat_tool')");
            t.HasCheckConstraint("ck_ai_strategy_drafts_yaml", "status <> 'valid' OR yaml IS NOT NULL");
        });
        builder.HasKey(x => x.Id).HasName("pk_strategy_drafts");
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.Idea).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(12).IsRequired();
        builder.Property(x => x.FailReason).HasMaxLength(24);
        builder.Property(x => x.Via).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Yaml).HasColumnType("text");
        builder.Property(x => x.AnswerJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.AttemptsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ProblemsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FindingsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(80);
        builder.Property(x => x.ModelSha256).HasColumnType("character(64)");
        builder.Property(x => x.PromptVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SheetHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ParamsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.RequestedBy).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("ix_ai_strategy_drafts_created_at");
    }
}
