using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Planning;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>trading.plans</c> (spec P7-03, ADR 0083): the immutable header of a published plan. Insert-only for the runtime role.</summary>
internal sealed class PlanRecordConfiguration : IEntityTypeConfiguration<PlanRecord>
{
    public void Configure(EntityTypeBuilder<PlanRecord> builder)
    {
        builder.ToTable("plans", Schemas.Trading, t => t.HasCheckConstraint("ck_plans_shape", "version >= 1 AND length(plan_key) > 0 AND length(input_hash) = 64"));
        builder.HasKey(x => x.Id).HasName("pk_plans");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.PlanKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Basis).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Notice).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Limits).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.PlanKey).IsUnique().HasDatabaseName("uq_plans_plan_key");
        builder.HasIndex(x => new { x.ForDate, x.Version }).IsUnique().HasDatabaseName("uq_plans_for_date_version");
    }
}

/// <summary><c>trading.plan_items</c>: the candidates of a plan, immutable. Reserve and rejected carry reasons.</summary>
internal sealed class PlanItemRecordConfiguration : IEntityTypeConfiguration<PlanItemRecord>
{
    public void Configure(EntityTypeBuilder<PlanItemRecord> builder)
    {
        builder.ToTable("plan_items", Schemas.Trading, t =>
        {
            t.HasCheckConstraint("ck_plan_items_group", "item_group IN ('primary', 'reserve', 'rejected') AND (item_group = 'primary' OR cardinality(reasons) > 0)");
            t.HasCheckConstraint("ck_plan_items_shape", "item_rank >= 1 AND side IN ('BUY', 'SELL') AND trades >= 0 AND (p_win IS NULL OR (p_win >= 0 AND p_win <= 1))");
        });
        builder.HasKey(x => x.Id).HasName("pk_plan_items");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.ItemGroup).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Side).HasMaxLength(4).IsRequired();
        builder.Property(x => x.StrategyId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StrategyVersionHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TriggerText).HasMaxLength(500).IsRequired();
        builder.Property(x => x.StopText).HasMaxLength(500).IsRequired();
        builder.Property(x => x.TargetText).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ValidityText).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CellKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DataRange).HasMaxLength(300).IsRequired();
        builder.Property(x => x.PWinBasis).HasMaxLength(300).IsRequired();
        builder.Property(x => x.AdvisoryNote).HasMaxLength(300);
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_plan_items_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PlanId, x.ItemRank }).IsUnique().HasDatabaseName("uq_plan_items_plan_rank");
    }
}

/// <summary><c>trading.plan_item_allocations</c>: size per Trading account. Paper mode only until the owner decides otherwise (P7-00 question 2): a later migration relaxes the check.</summary>
internal sealed class PlanItemAllocationRecordConfiguration : IEntityTypeConfiguration<PlanItemAllocationRecord>
{
    public void Configure(EntityTypeBuilder<PlanItemAllocationRecord> builder)
    {
        builder.ToTable("plan_item_allocations", Schemas.Trading, t => t.HasCheckConstraint("ck_plan_item_allocations_shape", "quantity > 0 AND mode = 'paper'"));
        builder.HasKey(x => x.Id).HasName("pk_plan_item_allocations");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Mode).HasMaxLength(16).IsRequired();
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_plan_item_allocations_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("fk_plan_item_allocations_broker_accounts_account_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ItemId, x.AccountId }).IsUnique().HasDatabaseName("uq_plan_item_allocations_item_account");
        builder.HasIndex(x => x.AccountId).HasDatabaseName("ix_plan_item_allocations_account_id");
    }
}

/// <summary>
/// <c>trading.plan_events</c>: the trail of state changes. The state is the newest row; the check lists the legal pairs of the two tables, so the database refuses a move that the Domain tables do not have (a test compares them).
/// </summary>
internal sealed class PlanEventRecordConfiguration : IEntityTypeConfiguration<PlanEventRecord>
{
    public static string LegalPairs { get; } =
        "(item_id IS NOT NULL AND (from_state, to_state) IN (" + string.Join(", ", TicketLifecycle.Moves.Select(m => $"('{m.From.Code()}', '{m.To.Code()}')")) + "))"
        + " OR (item_id IS NULL AND (from_state, to_state) IN (" + string.Join(", ", PlanLifecycle.Moves.Select(m => $"('{m.From.Code()}', '{m.To.Code()}')")) + "))";

    public void Configure(EntityTypeBuilder<PlanEventRecord> builder)
    {
        builder.ToTable("plan_events", Schemas.Trading, t =>
        {
            t.HasCheckConstraint("ck_plan_events_legal", LegalPairs);
            t.HasCheckConstraint("ck_plan_events_shape", "length(btrim(actor)) > 0 AND length(btrim(reason)) > 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_plan_events");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.FromState).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ToState).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_plan_events_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_plan_events_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApprovalRecord>().WithMany().HasForeignKey(x => x.ApprovalId).HasConstraintName("fk_plan_events_approvals_approval_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PlanId, x.Id }).HasDatabaseName("ix_plan_events_plan_id_id");
        builder.HasIndex(x => new { x.ItemId, x.Id }).HasDatabaseName("ix_plan_events_item_id_id");
    }
}

/// <summary><c>workflow.approvals</c>: the owner's decisions. The only kind is <c>owner</c> (a check refuses any other): there is no automatic approval.</summary>
internal sealed class ApprovalRecordConfiguration : IEntityTypeConfiguration<ApprovalRecord>
{
    public void Configure(EntityTypeBuilder<ApprovalRecord> builder)
    {
        builder.ToTable("approvals", Schemas.Workflow, t =>
        {
            t.HasCheckConstraint("ck_approvals_kind", "kind = 'owner'");
            t.HasCheckConstraint("ck_approvals_shape", "decision IN ('approve', 'modify', 'reject') AND (decision = 'modify') = (modifications IS NOT NULL) AND (confirm_reason IS NULL OR decision = 'approve') AND length(btrim(actor)) > 0 AND length(btrim(reason)) > 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_approvals");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Decision).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ConfirmReason).HasMaxLength(500);
        builder.Property(x => x.Modifications).HasMaxLength(1000);
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_approvals_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_approvals_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PlanId, x.Id }).HasDatabaseName("ix_approvals_plan_id_id");
    }
}

/// <summary><c>trading.plan_runs</c>: one row per pipeline attempt. A published run names its plan; a failed-closed run names the gate and has no plan.</summary>
internal sealed class PlanRunRecordConfiguration : IEntityTypeConfiguration<PlanRunRecord>
{
    public void Configure(EntityTypeBuilder<PlanRunRecord> builder)
    {
        builder.ToTable("plan_runs", Schemas.Trading, t =>
        {
            t.HasCheckConstraint("ck_plan_runs_outcome", "outcome IN ('published', 'failed_closed', 'dry_run') AND (outcome = 'published') = (plan_id IS NOT NULL) AND (outcome = 'failed_closed') = (failed_gate IS NOT NULL) AND finished_at >= started_at");
        });
        builder.HasKey(x => x.Id).HasName("pk_plan_runs");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.PlanKey).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FailedGate).HasMaxLength(40);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_plan_runs_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ForDate, x.Id }).HasDatabaseName("ix_plan_runs_for_date_id");
    }
}
