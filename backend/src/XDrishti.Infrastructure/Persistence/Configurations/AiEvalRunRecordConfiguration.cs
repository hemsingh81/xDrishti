using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.eval_runs</c> (spec P9-09): insert-only. A gating run needs at least two repeats; a baseline row can only follow a PASS (checked in the application and by the check below for the verdict vocabulary).</summary>
internal sealed class AiEvalRunRecordConfiguration : IEntityTypeConfiguration<AiEvalRunRecord>
{
    public void Configure(EntityTypeBuilder<AiEvalRunRecord> builder)
    {
        builder.ToTable("eval_runs", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_eval_runs_verdict", "verdict IN ('PASS', 'FAIL', 'INCOMPLETE')");
            t.HasCheckConstraint("ck_ai_eval_runs_repeats", "repeats BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_ai_eval_runs_gating", "NOT gating OR (repeats >= 2 AND target = 'builtin' AND status = 'completed')");
            t.HasCheckConstraint("ck_ai_eval_runs_time", "finished_at >= started_at");
        });
        builder.HasKey(x => x.Id).HasName("pk_eval_runs");
        builder.Property(x => x.Target).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Profile).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Sets).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ModelSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RuntimeDigest).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ParamsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PromptVersionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SetsHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ThresholdsHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.FixtureHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Fingerprint).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Verdict).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SummaryJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.FinishedAt).IsDescending().HasDatabaseName("ix_ai_eval_runs_finished_at");
        builder.HasIndex(x => x.Gating).HasDatabaseName("ix_ai_eval_runs_gating");
    }
}
