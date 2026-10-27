using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.settings</c> (spec P9-02, ADR 0137): insert-only for the runtime role; the newest row is current, no row means off (fail closed). Pattern of <c>workflow.autonomy_settings</c> (ADR 0091).</summary>
internal sealed class AiSettingRecordConfiguration : IEntityTypeConfiguration<AiSettingRecord>
{
    public void Configure(EntityTypeBuilder<AiSettingRecord> builder)
    {
        builder.ToTable("settings", Schemas.Ai, t => t.HasCheckConstraint("ck_ai_settings_shape", "max_tool_calls >= 1 AND max_tool_calls <= 12 AND changed_by LIKE 'owner:%' AND length(btrim(changed_by)) > 6 AND length(btrim(reason)) >= 1 AND length(reason) <= 500"));
        builder.HasKey(x => x.Id).HasName("pk_settings");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.AllowDraftConfirm).HasDefaultValue(false);
        builder.Property(x => x.AutoLabQuickTest).HasDefaultValue(false);
        builder.Property(x => x.AllowProposalAccept).HasDefaultValue(false);
        builder.Property(x => x.AutoAcceptHypothesis).HasDefaultValue(false);
        builder.Property(x => x.ChangedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SnapshotHash).HasColumnType("character(64)").IsRequired();
    }
}
