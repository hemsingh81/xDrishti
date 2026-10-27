using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.tool_calls</c> (spec P9-03): one row per tool call of a turn (<c>call_index</c> unique per turn).</summary>
internal sealed class AiToolCallRecordConfiguration : IEntityTypeConfiguration<AiToolCallRecord>
{
    public void Configure(EntityTypeBuilder<AiToolCallRecord> builder)
    {
        builder.ToTable("tool_calls", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_tool_calls_status", "status IN ('ok', 'error', 'timeout', 'refused', 'disabled')");
            t.HasCheckConstraint("ck_ai_tool_calls_result_bytes", "result_bytes >= 0 AND result_bytes <= 10485760");
        });
        builder.HasKey(x => x.Id).HasName("pk_tool_calls");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.ToolName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Arguments).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(60);
        builder.Property(x => x.Result).HasColumnType("jsonb");
        builder.Property(x => x.Evidence).HasColumnType("jsonb");
        builder.HasOne<AiConversationRecord>().WithMany().HasForeignKey(x => x.ConversationId).HasConstraintName("fk_tool_calls_conversations_conversation_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ConversationId, x.TurnId, x.CallIndex }).IsUnique().HasDatabaseName("ux_ai_tool_calls_conversation_id_turn_id_call_index");
    }
}
