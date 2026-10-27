using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.eval_baselines</c> (spec P9-09 AC 11): at most one row per run (unique); insert-only. Who accepted it and, when a set got worse, why.</summary>
internal sealed class AiEvalBaselineRecordConfiguration : IEntityTypeConfiguration<AiEvalBaselineRecord>
{
    public void Configure(EntityTypeBuilder<AiEvalBaselineRecord> builder)
    {
        builder.ToTable("eval_baselines", Schemas.Ai, t => t.HasCheckConstraint("ck_ai_eval_baselines_actor", "accepted_by = 'cli:xd'"));
        builder.HasKey(x => x.Id).HasName("pk_eval_baselines");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.AcceptedBy).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.AcceptedAt).HasDefaultValueSql("now()");
        builder.HasOne<AiEvalRunRecord>().WithMany().HasForeignKey(x => x.RunId).HasConstraintName("fk_eval_baselines_eval_runs_run_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.RunId).IsUnique().HasDatabaseName("ux_ai_eval_baselines_run_id");
    }
}
