using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.briefing_attempts</c> (spec P9-06): the model attempts of a briefing. Insert-only; the briefing row is restricted (never cascaded).</summary>
internal sealed class AiBriefingAttemptRecordConfiguration : IEntityTypeConfiguration<AiBriefingAttemptRecord>
{
    public void Configure(EntityTypeBuilder<AiBriefingAttemptRecord> builder)
    {
        builder.ToTable("briefing_attempts", Schemas.Ai, t => t.HasCheckConstraint("ck_ai_briefing_attempts_attempt", "attempt >= 1"));
        builder.HasKey(x => x.Id).HasName("pk_briefing_attempts");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Text).HasColumnType("text").IsRequired();
        builder.Property(x => x.FindingsJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<AiBriefingRecord>().WithMany().HasForeignKey(x => x.BriefingId).HasConstraintName("fk_briefing_attempts_briefings_briefing_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AiModelCallRecord>().WithMany().HasForeignKey(x => x.ModelCallId).HasConstraintName("fk_briefing_attempts_model_calls_model_call_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BriefingId, x.Attempt }).IsUnique().HasDatabaseName("ux_ai_briefing_attempts_briefing_id_attempt");
        builder.HasIndex(x => x.ModelCallId).HasDatabaseName("ix_briefing_attempts_model_call_id");
    }
}
