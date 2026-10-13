using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.action_states</c>: the owner's decisions about Action-center items (ADR 0022), one row per rule and item key. A decision that stops applying becomes <c>cleared</c> (an update); the runtime role has no DELETE.
/// </summary>
internal sealed class ActionStateConfiguration : IEntityTypeConfiguration<ActionState>
{
    public void Configure(EntityTypeBuilder<ActionState> builder)
    {
        builder.ToTable("action_states", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_action_states_state", "state IN ('snoozed', 'done', 'cleared')");

            // A snooze has the date it ends; no other state has one.
            t.HasCheckConstraint("ck_action_states_snoozed_until", "(state = 'snoozed') = (snoozed_until IS NOT NULL)");
        });
        builder.HasKey(x => new { x.Rule, x.Key });
        builder.Property(x => x.Rule).HasMaxLength(ActionState.MaxRuleLength).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(ActionState.MaxKeyLength).ValueGeneratedNever();
        builder.Property(x => x.State).HasConversion(EnumConversions.Lowercase<ActionDecision>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.SnoozedUntil);
        builder.Property(x => x.UpdatedBy).HasMaxLength(ActionState.MaxUpdatedByLength).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
