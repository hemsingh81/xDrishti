using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>workflow.assistant_proposal_decisions</c> (spec P9-08): one decision per proposal, unique. Insert-only; the proposal is restricted (never cascaded). The auto flag and the auto actor are tied both ways by a check.</summary>
internal sealed class AssistantProposalDecisionRecordConfiguration : IEntityTypeConfiguration<AssistantProposalDecisionRecord>
{
    public void Configure(EntityTypeBuilder<AssistantProposalDecisionRecord> builder)
    {
        builder.ToTable("assistant_proposal_decisions", Schemas.Workflow, t =>
        {
            t.HasCheckConstraint("ck_assistant_proposal_decisions_decision", "decision IN ('accepted', 'rejected')");
            t.HasCheckConstraint("ck_assistant_proposal_decisions_auto", "(actor = 'system:auto-accept') = auto AND (NOT auto OR (decision = 'accepted' AND settings_id IS NOT NULL AND snapshot_hash IS NOT NULL))");
            t.HasCheckConstraint("ck_assistant_proposal_decisions_result", "decision <> 'accepted' OR result_ref IS NOT NULL");
        });
        builder.HasKey(x => x.Id).HasName("pk_assistant_proposal_decisions");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Decision).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SnapshotHash).HasColumnType("character(64)");
        builder.Property(x => x.Reason).HasMaxLength(300);
        builder.Property(x => x.ResultRef).HasMaxLength(80);
        builder.Property(x => x.DecidedAt).HasDefaultValueSql("now()");
        builder.HasOne<AssistantProposalRecord>().WithMany().HasForeignKey(x => x.ProposalId).HasConstraintName("fk_assistant_proposal_decisions_assistant_proposals_proposal_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProposalId).IsUnique().HasDatabaseName("ux_assistant_proposal_decisions_proposal_id");
        builder.HasIndex(x => x.DecidedAt).HasDatabaseName("ix_assistant_proposal_decisions_decided_at");
    }
}
