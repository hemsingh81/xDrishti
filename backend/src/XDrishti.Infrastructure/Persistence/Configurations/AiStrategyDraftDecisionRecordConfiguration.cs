using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.strategy_draft_decisions</c> (spec P9-07): one decision per draft, unique. Insert-only; the draft is restricted (never cascaded).</summary>
internal sealed class AiStrategyDraftDecisionRecordConfiguration : IEntityTypeConfiguration<AiStrategyDraftDecisionRecord>
{
    public void Configure(EntityTypeBuilder<AiStrategyDraftDecisionRecord> builder)
    {
        builder.ToTable("strategy_draft_decisions", Schemas.Ai, t =>
        {
            t.HasCheckConstraint("ck_ai_strategy_draft_decisions_decision", "decision IN ('confirmed', 'discarded')");
            t.HasCheckConstraint("ck_ai_strategy_draft_decisions_confirmed", "decision <> 'confirmed' OR (strategy_id IS NOT NULL AND strategy_version IS NOT NULL)");
            t.HasCheckConstraint("ck_ai_strategy_draft_decisions_auto", "(actor = 'system:auto-draft') = auto AND (NOT auto OR (decision = 'confirmed' AND settings_id IS NOT NULL AND snapshot_hash IS NOT NULL))");
        });
        builder.HasKey(x => x.Id).HasName("pk_strategy_draft_decisions");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Decision).HasMaxLength(10).IsRequired();
        builder.Property(x => x.StrategyId).HasMaxLength(64);
        builder.Property(x => x.SnapshotHash).HasColumnType("character(64)");
        builder.Property(x => x.Actor).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RunError).HasMaxLength(64);
        builder.Property(x => x.DecidedAt).HasDefaultValueSql("now()");
        builder.HasOne<AiStrategyDraftRecord>().WithMany().HasForeignKey(x => x.DraftId).HasConstraintName("fk_strategy_draft_decisions_strategy_drafts_draft_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DraftId).IsUnique().HasDatabaseName("ux_ai_strategy_draft_decisions_draft_id");
        builder.HasIndex(x => x.DecidedAt).HasDatabaseName("ix_ai_strategy_draft_decisions_decided_at");
    }
}
