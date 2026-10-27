using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.model_calls</c> (spec P9-03): model calls outside chat. No content.</summary>
internal sealed class AiModelCallRecordConfiguration : IEntityTypeConfiguration<AiModelCallRecord>
{
    public void Configure(EntityTypeBuilder<AiModelCallRecord> builder)
    {
        builder.ToTable("model_calls", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_model_calls_purpose", "purpose IN ('briefing', 'draft', 'proposal', 'eval', 'other')");
            t.HasCheckConstraint("ck_ai_model_calls_status", "status IN ('ok', 'error', 'timeout', 'cancelled')");
        });
        builder.HasKey(x => x.Id).HasName("pk_model_calls");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Purpose).HasMaxLength(12).IsRequired();
        builder.Property(x => x.Caller).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PromptHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ModelSha256).HasColumnType("character(64)");
        builder.Property(x => x.FinishReason).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(60);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
    }
}
