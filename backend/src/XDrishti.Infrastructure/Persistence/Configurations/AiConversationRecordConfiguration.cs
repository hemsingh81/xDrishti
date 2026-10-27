using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.conversations</c> (spec P9-03): the conversation, with the origin of its turns (chat, eval, job).</summary>
internal sealed class AiConversationRecordConfiguration : IEntityTypeConfiguration<AiConversationRecord>
{
    public void Configure(EntityTypeBuilder<AiConversationRecord> builder)
    {
        builder.ToTable("conversations", Schemas.Ai, t => t.HasCheckConstraint("ck_ai_conversations_origin", "origin IN ('chat', 'eval', 'job')"));
        builder.HasKey(x => x.Id).HasName("pk_conversations");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Origin).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.LlmBuild).HasMaxLength(60);
        builder.HasOne<AiSettingRecord>().WithMany().HasForeignKey(x => x.SettingsId).HasConstraintName("fk_conversations_settings_settings_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("ix_ai_conversations_created_at");
    }
}
