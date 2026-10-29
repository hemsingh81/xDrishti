using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>trading.plans</c> (spec P7-03, ADR 0083): the immutable header of a published plan. Insert-only for the runtime role.</summary>
internal sealed class PlanRecordConfiguration : IEntityTypeConfiguration<PlanRecord>
{
    public void Configure(EntityTypeBuilder<PlanRecord> builder)
    {
        builder.ToTable("plans", Schemas.Trading, t =>
        {
            t.HasCheckConstraint("ck_plans_shape", "version >= 1 AND length(plan_key) > 0 AND length(input_hash) = 64 AND (review_cutoff AT TIME ZONE 'Asia/Kolkata')::date = for_date");
            t.HasCheckConstraint("ck_plans_flags_json", "basis_flags_json IS NULL OR left(basis_flags_json, 1) = '{'");
        });
        builder.HasKey(x => x.Id).HasName("pk_plans");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.PlanKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReviewCutoff).IsRequired();
        builder.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Basis).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Notice).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Limits).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.BasisFlagsJson);
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.PlanKey).IsUnique().HasDatabaseName("uq_plans_plan_key");
        builder.HasIndex(x => new { x.Id, x.ReviewCutoff }).IsUnique().HasDatabaseName("uq_plans_id_review_cutoff");
        builder.HasIndex(x => new { x.ForDate, x.Version }).IsUnique().HasDatabaseName("uq_plans_for_date_version");
    }
}

/// <summary><c>trading.plan_items</c>: the candidates of a plan, immutable. Reserve and rejected carry reasons.</summary>
internal sealed class PlanItemRecordConfiguration : IEntityTypeConfiguration<PlanItemRecord>
{
    // P7-19: all six null, or an entry reference with its session; a stop has an anchor and the right side of the entry; a target needs a stop, the right side and a reward-to-risk (and the converse).
    internal const string LevelsCheck = "(entry_ref_price IS NULL AND levels_session IS NULL AND stop_level IS NULL AND stop_anchor IS NULL AND target_level IS NULL AND reward_risk IS NULL)"
        + " OR (entry_ref_price IS NOT NULL AND entry_ref_price > 0 AND levels_session IS NOT NULL AND (stop_level IS NULL) = (stop_anchor IS NULL) AND (stop_anchor IS NULL OR stop_anchor IN ('level', 'close_of_d'))"
        + " AND (stop_level IS NULL OR (stop_level > 0 AND ((side = 'BUY' AND stop_level < entry_ref_price) OR (side = 'SELL' AND stop_level > entry_ref_price))))"
        + " AND (target_level IS NULL) = (reward_risk IS NULL) AND (target_level IS NULL OR (stop_level IS NOT NULL AND target_level > 0 AND reward_risk > 0 AND ((side = 'BUY' AND target_level > entry_ref_price) OR (side = 'SELL' AND target_level < entry_ref_price)))))";

    public void Configure(EntityTypeBuilder<PlanItemRecord> builder)
    {
        builder.ToTable("plan_items", Schemas.Trading, t =>
        {
            t.HasCheckConstraint("ck_plan_items_group", "item_group IN ('primary', 'reserve', 'rejected') AND (item_group = 'primary' OR cardinality(reasons) > 0)");
            t.HasCheckConstraint("ck_plan_items_p_win_lower", "p_win_lower IS NULL OR (p_win_lower >= 0 AND p_win_lower <= 1)");
            t.HasCheckConstraint("ck_plan_items_shape", "item_rank >= 1 AND side IN ('BUY', 'SELL') AND trades >= 0 AND (p_win IS NULL OR (p_win >= 0 AND p_win <= 1))");
            t.HasCheckConstraint("ck_plan_items_levels", PlanItemRecordConfiguration.LevelsCheck);
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
        builder.Property(x => x.EntryRefPrice).HasPrecision(18, 4);
        builder.Property(x => x.StopLevel).HasPrecision(18, 4);
        builder.Property(x => x.StopAnchor).HasMaxLength(12);
        builder.Property(x => x.TargetLevel).HasPrecision(18, 4);
        builder.Property(x => x.RewardRisk).HasPrecision(9, 4);
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_plan_items_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PlanId, x.ItemRank }).IsUnique().HasDatabaseName("uq_plan_items_plan_rank");
    }
}

/// <summary><c>trading.plan_item_allocations</c>: size per Trading account. Paper, or manual live with its ramp scale and ramp event (P11-04, ADR 0148); P12-07 extends the later modes and must keep both requirements.</summary>
internal sealed class PlanItemAllocationRecordConfiguration : IEntityTypeConfiguration<PlanItemAllocationRecord>
{
    public void Configure(EntityTypeBuilder<PlanItemAllocationRecord> builder)
    {
        builder.ToTable("plan_item_allocations", Schemas.Trading, t => t.HasCheckConstraint("ck_plan_item_allocations_shape", "quantity > 0 AND (mode = 'paper' OR (mode = 'manual_live' AND risk_scale > 0 AND risk_scale <= 1 AND ramp_event_id IS NOT NULL))"));
        builder.HasKey(x => x.Id).HasName("pk_plan_item_allocations");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Mode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RiskScale).HasPrecision(5, 4);
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

            // The database enforces owner-only itself: a ticket event into approved, modified or rejected names an approval row, and no other event does (the approval row is `owner` by its own checks).
            // The first event of an item starts at proposed and of a plan at draft, and only the first event has no previous event; every later one continues from the to_state of the one before (composite foreign key in the migration).
            t.HasCheckConstraint("ck_plan_events_chain", "(item_id IS NULL AND (prev_event_id IS NULL) = (from_state = 'draft')) OR (item_id IS NOT NULL AND (prev_event_id IS NULL) = (from_state = 'proposed'))");
            t.HasCheckConstraint("ck_plan_events_approval", "(item_id IS NULL AND approval_id IS NULL) OR (item_id IS NOT NULL AND ((to_state IN ('approved', 'modified', 'rejected')) = (approval_id IS NOT NULL)))");
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
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_plan_events_created_at"); // the audit screen (spec P8-22)

        // Continuity: a state is left at most once, so the trail of an item or plan is one chain even without the lock.
        builder.HasIndex(x => new { x.ItemId, x.FromState }).IsUnique().HasFilter("item_id IS NOT NULL").HasDatabaseName("uq_plan_events_item_from");
        builder.HasIndex(x => new { x.PlanId, x.FromState }).IsUnique().HasFilter("item_id IS NULL").HasDatabaseName("uq_plan_events_plan_from");
    }
}

/// <summary><c>workflow.approvals</c>: the owner's decisions. The only kind is <c>owner</c> (a check refuses any other): there is no automatic approval.</summary>
internal sealed class ApprovalRecordConfiguration : IEntityTypeConfiguration<ApprovalRecord>
{
    // An owner row: an owner actor and no settings columns. An auto row (P7-12, ADR 0091): approve only, the rule as actor (never an owner prefix), the settings snapshot and the policy version, nothing the owner alone gives (modifications, confirmation).
    internal const string ApprovalShape = "decision IN ('approve', 'modify', 'reject') AND (decision = 'modify') = (modifications IS NOT NULL) AND (confirm_reason IS NULL OR decision = 'approve') AND length(btrim(reason)) > 0 AND (decision = 'reject' OR decided_at < cutoff) AND ((kind = 'owner' AND actor LIKE 'owner:%' AND settings_id IS NULL AND settings_hash IS NULL AND policy_version IS NULL) OR (kind = 'auto' AND actor = 'rule:autonomy' AND decision = 'approve' AND modifications IS NULL AND confirm_reason IS NULL AND settings_id IS NOT NULL AND settings_hash IS NOT NULL AND policy_version IS NOT NULL AND item_id IS NOT NULL))";

    public void Configure(EntityTypeBuilder<ApprovalRecord> builder)
    {
        builder.ToTable("approvals", Schemas.Workflow, t =>
        {
            t.HasCheckConstraint("ck_approvals_kind", "kind IN ('owner', 'auto')");
            t.HasCheckConstraint("ck_approvals_shape", ApprovalShape);
        });
        builder.HasKey(x => x.Id).HasName("pk_approvals");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Decision).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ConfirmReason).HasMaxLength(500);
        builder.Property(x => x.Modifications).HasMaxLength(1000);
        builder.Property(x => x.SettingsHash).HasMaxLength(64);
        builder.Property(x => x.PolicyVersion).HasMaxLength(40);
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_approvals_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_approvals_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PlanId, x.Id }).HasDatabaseName("ix_approvals_plan_id_id");
        builder.HasIndex(x => x.DecidedAt).HasDatabaseName("ix_approvals_decided_at"); // the audit screen (spec P8-22)
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
        builder.HasIndex(x => x.ForDate).IsUnique().HasFilter("outcome = 'published'").HasDatabaseName("uq_plan_runs_published_for_date"); // a planned session has at most one published run (P7-08)
    }
}

/// <summary><c>trading.grades</c> (spec P7-13, ADR 0090): the grade of one ticket, one per ticket, insert-only for the runtime role.</summary>
internal sealed class GradeRecordConfiguration : IEntityTypeConfiguration<GradeRecord>
{
    public void Configure(EntityTypeBuilder<GradeRecord> builder)
    {
        builder.ToTable("grades", Schemas.Trading, t =>
        {
            t.HasCheckConstraint("ck_grades_status", "status IN ('graded', 'not_triggered', 'not_gradable') AND basis = 'simulated on plan prices' AND ((status = 'graded') = (r IS NOT NULL)) AND ((status = 'graded') = (exit_reason IS NOT NULL)) AND length(detail) > 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_grades");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ExitReason).HasMaxLength(32);
        builder.Property(x => x.CostBasis).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Basis).HasMaxLength(40).IsRequired();
        builder.Property(x => x.RunKey).HasMaxLength(64);
        builder.Property(x => x.StopPrice);
        builder.HasIndex(x => x.ItemId).IsUnique().HasDatabaseName("uq_grades_item_id"); // one grade per ticket; the second insert is skipped
        builder.HasIndex(x => new { x.ForDate, x.PlanId }).HasDatabaseName("ix_grades_for_date_plan_id");
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_grades_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_grades_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>trading.journal_entries</c> (spec P7-14, ADR 0093): a ticket-on-an-account the owner's fills were matched to. Insert-only; one per ticket and account, tied to a real allocation.</summary>
internal sealed class JournalEntryRecordConfiguration : IEntityTypeConfiguration<JournalEntryRecord>
{
    public void Configure(EntityTypeBuilder<JournalEntryRecord> builder)
    {
        builder.ToTable("journal_entries", Schemas.Trading);
        builder.Property(x => x.UncertainReason).HasMaxLength(300);
        builder.HasKey(x => x.Id).HasName("pk_journal_entries");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.HasIndex(x => new { x.ItemId, x.AccountId }).IsUnique().HasDatabaseName("uq_journal_entries_item_account");
        builder.HasIndex(x => x.PlanId).HasDatabaseName("ix_journal_entries_plan_id");
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_journal_entries_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanItemRecord>().WithMany().HasForeignKey(x => x.ItemId).HasConstraintName("fk_journal_entries_plan_items_item_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("fk_journal_entries_broker_accounts_account_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>trading.journal_fills</c>: a stored trade (<c>portfolio.transactions</c>, read only) and its role. A trade is journaled once (unique), so it matches at most one ticket; a discretionary fill has no entry.</summary>
internal sealed class JournalFillRecordConfiguration : IEntityTypeConfiguration<JournalFillRecord>
{
    public void Configure(EntityTypeBuilder<JournalFillRecord> builder)
    {
        builder.ToTable("journal_fills", Schemas.Trading, t => t.HasCheckConstraint("ck_journal_fills_shape", "role IN ('entry', 'exit', 'discretionary') AND (role = 'discretionary') = (entry_id IS NULL) AND qty > 0 AND length(reason) > 0 AND (transaction_id IS NULL) <> (live_fill_id IS NULL)"));
        builder.HasKey(x => x.Id).HasName("pk_journal_fills");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Role).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(300).IsRequired();
        builder.HasIndex(x => x.TransactionId).IsUnique().HasDatabaseName("uq_journal_fills_transaction_id");
        builder.HasIndex(x => x.LiveFillId).IsUnique().HasDatabaseName("uq_journal_fills_live_fill_id"); // the foreign key onto trading.live_fills is raw SQL in the migration (that table is not in the model)
        builder.HasIndex(x => x.EntryId).HasDatabaseName("ix_journal_fills_entry_id");
        builder.HasIndex(x => x.ForDate).HasDatabaseName("ix_journal_fills_for_date");
        builder.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransactionId).HasConstraintName("fk_journal_fills_transactions_transaction_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntryRecord>().WithMany().HasForeignKey(x => x.EntryId).HasConstraintName("fk_journal_fills_journal_entries_entry_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>trading.fill_grades</c>: the grade of realised fills beside the plan grade. One per entry, insert-only, always labelled <c>realised fills, not simulated</c>.</summary>
internal sealed class FillGradeRecordConfiguration : IEntityTypeConfiguration<FillGradeRecord>
{
    public void Configure(EntityTypeBuilder<FillGradeRecord> builder)
    {
        builder.ToTable("fill_grades", Schemas.Trading, t => t.HasCheckConstraint("ck_fill_grades_shape", "status IN ('graded', 'no_plan_levels', 'uncertain') AND basis = 'realised fills, not simulated' AND qty > 0 AND ((status = 'graded') = (r IS NOT NULL)) AND ((status = 'graded') = (slippage_r IS NOT NULL)) AND length(detail) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_fill_grades");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.GrossPnl).HasPrecision(18, 2);
        builder.Property(x => x.Charges).HasPrecision(18, 2);
        builder.Property(x => x.Basis).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.EntryId).IsUnique().HasDatabaseName("uq_fill_grades_entry_id");
        builder.HasOne<JournalEntryRecord>().WithMany().HasForeignKey(x => x.EntryId).HasConstraintName("fk_fill_grades_journal_entries_entry_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>workflow.autonomy_settings</c> (spec P7-12, ADR 0091): every change of the autonomy switches, appended by the owner's act and never edited; the newest row is current, no row means everything OFF. The hash and time are stamped by a trigger.</summary>
internal sealed class AutonomySettingRecordConfiguration : IEntityTypeConfiguration<AutonomySettingRecord>
{
    public void Configure(EntityTypeBuilder<AutonomySettingRecord> builder)
    {
        builder.ToTable("autonomy_settings", Schemas.Workflow, t =>
        {
            t.HasCheckConstraint("ck_autonomy_settings_shape", "min_p_win >= 0.5 AND min_p_win <= 0.99 AND max_auto_per_day >= 1 AND max_auto_per_day <= 20 AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_autonomy_settings");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.MinPWin).HasPrecision(4, 3);
        builder.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.Id, x.SnapshotHash }).IsUnique().HasDatabaseName("uq_autonomy_settings_id_hash");
    }
}

/// <summary><c>workflow.suggestions</c> (spec P7-09, ADR 0092): the owner's suggestion inbox. Insert-only for the runtime role; the same plan, kind and subject is stored once.</summary>
internal sealed class SuggestionRecordConfiguration : IEntityTypeConfiguration<SuggestionRecord>
{
    public void Configure(EntityTypeBuilder<SuggestionRecord> builder)
    {
        builder.ToTable("suggestions", Schemas.Workflow, t => t.HasCheckConstraint("ck_suggestions_shape", "length(btrim(kind)) > 0 AND length(btrim(subject)) > 0 AND length(btrim(text)) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_suggestions");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.Kind, x.Subject }).IsUnique().HasDatabaseName("uq_suggestions_plan_kind_subject"); // idempotent: the second insert is skipped
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(x => x.PlanId).HasConstraintName("fk_suggestions_plans_plan_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>workflow.suggestion_seen</c> (spec P7-10, ADR 0095): the owner marked a suggestion as read. Insert-only for the runtime role; one mark per suggestion. The suggestion itself is never changed.</summary>
internal sealed class SuggestionSeenRecordConfiguration : IEntityTypeConfiguration<SuggestionSeenRecord>
{
    public void Configure(EntityTypeBuilder<SuggestionSeenRecord> builder)
    {
        builder.ToTable("suggestion_seen", Schemas.Workflow, t => t.HasCheckConstraint("ck_suggestion_seen_actor", "actor LIKE 'owner:%'"));
        builder.HasKey(x => x.Id).HasName("pk_suggestion_seen");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.SuggestionId).IsUnique().HasDatabaseName("uq_suggestion_seen_suggestion_id");
        builder.HasOne<SuggestionRecord>().WithMany().HasForeignKey(x => x.SuggestionId).HasConstraintName("fk_suggestion_seen_suggestions_suggestion_id").OnDelete(DeleteBehavior.Restrict);
    }
}
