using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>trading.ticket_alerts</c> (spec P7-20, ADR 0115): a crossing of a watched ticket's stored level. Insert-only; one per (ticket, kind); only the two kinds, only positive numbers.</summary>
internal sealed class TicketAlertRecordConfiguration : IEntityTypeConfiguration<TicketAlertRecord>
{
    public void Configure(EntityTypeBuilder<TicketAlertRecord> builder)
    {
        builder.ToTable("ticket_alerts", Schemas.Trading, t => t.HasCheckConstraint("ck_ticket_alerts_shape", "kind IN ('stop_crossed', 'target_crossed') AND level > 0 AND price > 0 AND (stop_anchor IS NULL OR stop_anchor IN ('level', 'close_of_d'))"));
        builder.HasKey(x => x.Id).HasName("pk_ticket_alerts");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.StopAnchor).HasMaxLength(12);
        builder.Property(x => x.Level).HasPrecision(18, 4);
        builder.Property(x => x.Price).HasPrecision(18, 4);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.ItemId, x.Kind }).IsUnique().HasDatabaseName("uq_ticket_alerts_item_kind");
        builder.HasIndex(x => x.Session).HasDatabaseName("ix_ticket_alerts_session");
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_ticket_alerts_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_ticket_alerts_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>trading.ticket_prompts</c>: one question per (ticket, paper allocation account), tied to a real allocation. Insert-only.</summary>
internal sealed class TicketPromptRecordConfiguration : IEntityTypeConfiguration<TicketPromptRecord>
{
    public void Configure(EntityTypeBuilder<TicketPromptRecord> builder)
    {
        builder.ToTable("ticket_prompts", Schemas.Trading);
        builder.HasKey(x => x.Id).HasName("pk_ticket_prompts");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.ItemId, x.AccountId }).IsUnique().HasDatabaseName("uq_ticket_prompts_item_account");
        builder.HasIndex(x => x.Session).HasDatabaseName("ix_ticket_prompts_session");
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_ticket_prompts_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_ticket_prompts_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>trading.ticket_notice_acks</c>: the owner's snooze, dismiss and mute. Insert-only; the actor must be <c>owner:</c> plus an address; each action has its own shape.</summary>
internal sealed class TicketNoticeAckRecordConfiguration : IEntityTypeConfiguration<TicketNoticeAckRecord>
{
    public void Configure(EntityTypeBuilder<TicketNoticeAckRecord> builder)
    {
        builder.ToTable("ticket_notice_acks", Schemas.Trading, t => t.HasCheckConstraint(
            "ck_ticket_notice_acks_shape",
            "notice_kind IN ('crossing', 'prompt', 'ticket') AND action IN ('snooze', 'dismiss', 'mute') AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND ("
            + "(action = 'snooze' AND notice_kind IN ('crossing', 'prompt') AND notice_id IS NOT NULL AND until IS NOT NULL AND item_id IS NULL) OR "
            + "(action = 'dismiss' AND notice_kind IN ('crossing', 'prompt') AND notice_id IS NOT NULL AND until IS NULL AND item_id IS NULL) OR "
            + "(action = 'mute' AND notice_kind = 'ticket' AND item_id IS NOT NULL AND notice_id IS NULL AND until IS NULL))"));
        builder.HasKey(x => x.Id).HasName("pk_ticket_notice_acks");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.NoticeKind).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.At).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.NoticeKind, x.NoticeId }).HasDatabaseName("ix_ticket_notice_acks_notice");
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_ticket_notice_acks_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
    }
}
