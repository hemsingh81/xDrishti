using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>workflow.assistant_proposals</c> (spec P9-08): the inbox of the assistant's proposals. Insert-only; one row per (key, India date). Not the plan-bound <c>workflow.suggestions</c> of P7-09.</summary>
internal sealed class AssistantProposalRecordConfiguration : IEntityTypeConfiguration<AssistantProposalRecord>
{
    public void Configure(EntityTypeBuilder<AssistantProposalRecord> builder)
    {
        builder.ToTable("assistant_proposals", Schemas.Workflow, t =>
        {
            t.HasCheckConstraint("ck_assistant_proposals_kind", "kind IN ('hypothesis', 'basket_add', 'basket_remove')");
            t.HasCheckConstraint("ck_assistant_proposals_shape", "length(dedupe_key) = 64 AND length(btrim(reason)) > 0 AND length(btrim(subject)) > 0 AND jsonb_typeof(payload) = 'object' AND expires_at > created_at");
        });
        builder.HasKey(x => x.Id).HasName("pk_assistant_proposals");
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.Kind).HasMaxLength(24).IsRequired();
        builder.Property(x => x.DedupeKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(400).IsRequired();
        builder.Property(x => x.EvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ChecksJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PackHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(80);
        builder.Property(x => x.ModelSha256).HasColumnType("character(64)");
        builder.Property(x => x.PromptVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.DedupeKey, x.CreatedOn }).IsUnique().HasDatabaseName("ux_assistant_proposals_dedupe_key_created_on");
        builder.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("ix_assistant_proposals_created_at");
    }
}
