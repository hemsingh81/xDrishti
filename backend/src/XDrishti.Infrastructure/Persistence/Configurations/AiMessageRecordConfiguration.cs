using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.messages</c> (spec P9-03): user and assistant messages in order (<c>seq</c> unique per conversation).</summary>
internal sealed class AiMessageRecordConfiguration : IEntityTypeConfiguration<AiMessageRecord>
{
    public void Configure(EntityTypeBuilder<AiMessageRecord> builder)
    {
        builder.ToTable("messages", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_messages_role", "role IN ('user', 'assistant')");
            t.HasCheckConstraint("ck_ai_messages_finish_reason", "finish_reason IS NULL OR finish_reason IN ('stop', 'length', 'tool_limit', 'cancelled', 'error')");
            t.HasCheckConstraint("ck_ai_messages_grounding_status", "grounding_status IS NULL OR grounding_status IN ('grounded', 'ungrounded', 'no_numbers', 'not_checked')");
        });
        builder.HasKey(x => x.Id).HasName("pk_messages");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Role).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.PromptHash).HasColumnType("character(64)");
        builder.Property(x => x.ModelId).HasMaxLength(120);
        builder.Property(x => x.FinishReason).HasMaxLength(12);
        builder.Property(x => x.GroundingStatus).HasMaxLength(12);
        builder.Property(x => x.Ungrounded).HasColumnType("jsonb");
        builder.HasOne<AiConversationRecord>().WithMany().HasForeignKey(x => x.ConversationId).HasConstraintName("fk_messages_conversations_conversation_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AiPromptRecord>().WithMany().HasForeignKey(x => x.PromptHash).HasConstraintName("fk_messages_prompts_prompt_hash").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ConversationId, x.Seq }).IsUnique().HasDatabaseName("ux_ai_messages_conversation_id_seq");
        builder.HasIndex(x => x.TurnId).HasDatabaseName("ix_ai_messages_turn_id");
    }
}
