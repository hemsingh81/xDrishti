using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.eval_cases</c> (spec P9-09): one row per case repeat of a run; unique on (run, set, case, repeat); insert-only; the run is restricted (never cascaded).</summary>
internal sealed class AiEvalCaseRecordConfiguration : IEntityTypeConfiguration<AiEvalCaseRecord>
{
    public void Configure(EntityTypeBuilder<AiEvalCaseRecord> builder)
    {
        builder.ToTable("eval_cases", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_eval_cases_set", "\"set\" IN ('data', 'plans', 'ideas', 'events')");
            t.HasCheckConstraint("ck_ai_eval_cases_repeat", "repeat >= 1");
            t.HasCheckConstraint("ck_ai_eval_cases_counts", "tool_calls >= 0 AND latency_ms >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_eval_cases");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Set).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CaseId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Score).HasColumnType("numeric(6,4)");
        builder.Property(x => x.DetailJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Answer).HasColumnType("text").IsRequired();
        builder.HasOne<AiEvalRunRecord>().WithMany().HasForeignKey(x => x.RunId).HasConstraintName("fk_eval_cases_eval_runs_run_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RunId, x.Set, x.CaseId, x.Repeat }).IsUnique().HasDatabaseName("ux_ai_eval_cases_run_set_case_repeat");
    }
}
