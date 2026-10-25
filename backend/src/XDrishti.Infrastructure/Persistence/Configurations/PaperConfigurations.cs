using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Paper;
using XDrishti.Infrastructure.Paper;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>paper.epochs</c> (spec P10-01, ADR 0116): the periods a count belongs to. Insert-only for the runtime role.</summary>
internal sealed class PaperEpochRecordConfiguration : IEntityTypeConfiguration<PaperEpochRecord>
{
    public void Configure(EntityTypeBuilder<PaperEpochRecord> builder)
    {
        builder.ToTable("epochs", Schemas.Paper, t => t.HasCheckConstraint("ck_epochs_shape", "kind IN ('first', 'detected_change', 'owner_restart') AND length(btrim(reason)) > 0 AND length(btrim(actor)) > 0 AND (fingerprint_hash IS NULL OR length(fingerprint_hash) = 64)"));
        builder.HasKey(x => x.Id).HasName("pk_epochs");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.FingerprintHash).HasColumnType("char(64)");
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.HasOne<PaperEpochRecord>().WithMany().HasForeignKey(x => x.PrevEpochId).HasConstraintName("fk_epochs_epochs_prev_epoch_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.epoch_accounts</c>: the capital of each paper account in an epoch. Insert-only.</summary>
internal sealed class PaperEpochAccountRecordConfiguration : IEntityTypeConfiguration<PaperEpochAccountRecord>
{
    public void Configure(EntityTypeBuilder<PaperEpochAccountRecord> builder)
    {
        builder.ToTable("epoch_accounts", Schemas.Paper, t => t.HasCheckConstraint("ck_epoch_accounts_shape", "mode = 'paper' AND capital > 0"));
        builder.HasKey(x => x.Id).HasName("pk_epoch_accounts");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Capital).HasPrecision(18, 2);
        builder.Property(x => x.CapitalSource).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Mode).HasMaxLength(8).IsRequired();
        builder.HasIndex(x => new { x.EpochId, x.AccountId }).IsUnique().HasDatabaseName("uq_epoch_accounts_epoch_account");
        builder.HasOne<PaperEpochRecord>().WithMany().HasForeignKey(x => x.EpochId).HasConstraintName("fk_epoch_accounts_epochs_epoch_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("fk_epoch_accounts_broker_accounts_account_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// <c>paper.entries</c>: one paper trade of one account, one per ticket and account. The database refuses an entry before arming, a quantity above the allocated one and any basis but the label. The composite keys onto the plan record
/// (the ticket is in its plan; the allocation exists) are added in the migration.
/// </summary>
internal sealed class PaperEntryRecordConfiguration : IEntityTypeConfiguration<PaperEntryRecord>
{
    public void Configure(EntityTypeBuilder<PaperEntryRecord> builder)
    {
        builder.ToTable("entries", Schemas.Paper, t => t.HasCheckConstraint(
            "ck_entries_shape",
            "measure_status IN ('measured', 'straddle') AND ticket_group IN ('primary', 'reserve') AND approval_kind IN ('owner', 'auto') AND side IN ('BUY', 'SELL') AND qty > 0 AND qty <= qty_allocated AND entry_at >= armed_at AND basis = '" + PaperWording.Basis + "'"));
        builder.HasKey(x => x.Id).HasName("pk_entries");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.MeasureStatus).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TicketGroup).HasMaxLength(8).IsRequired();
        builder.Property(x => x.ApprovalKind).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Side).HasMaxLength(4).IsRequired();
        builder.Property(x => x.StrategyId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StrategyHash).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.CellKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.EntryPrice).HasPrecision(18, 6);
        builder.Property(x => x.EntryExact).HasPrecision(18, 6);
        builder.Property(x => x.StopPrice).HasPrecision(18, 6);
        builder.Property(x => x.TargetPrice).HasPrecision(18, 6);
        builder.Property(x => x.RunKey).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.EngineVersion).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Basis).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.PlanItemId, x.AccountId }).IsUnique().HasDatabaseName("uq_entries_item_account");
        builder.HasIndex(x => new { x.Id, x.EntryAt }).IsUnique().HasDatabaseName("uq_entries_id_entry_at");
        builder.HasIndex(x => new { x.EntryAt, x.Id }).HasDatabaseName("ix_entries_entry_at_id");
        builder.HasIndex(x => x.PlanId).HasDatabaseName("ix_entries_plan_id");
        builder.HasOne<PaperEpochRecord>().WithMany().HasForeignKey(x => x.EpochId).HasConstraintName("fk_entries_epochs_epoch_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApprovalRecord>().WithMany().HasForeignKey(x => x.ApprovalId).HasConstraintName("fk_entries_approvals_approval_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.exits</c>: the close of a paper trade, at most one per entry, tied to the entry's instant so an exit cannot precede it.</summary>
internal sealed class PaperExitRecordConfiguration : IEntityTypeConfiguration<PaperExitRecord>
{
    public void Configure(EntityTypeBuilder<PaperExitRecord> builder)
    {
        builder.ToTable("exits", Schemas.Paper, t => t.HasCheckConstraint(
            "ck_exits_shape",
            "exit_at >= entry_at AND held_sessions >= 1 AND r_basis IN ('net', 'gross') AND (charges IS NULL) = (r_net IS NULL) AND (charges IS NULL) = (net_pnl IS NULL) AND (charges IS NULL) = (charge_sheet_id IS NULL) AND ((r_basis = 'net') = (r_net IS NOT NULL))"));
        builder.HasKey(x => x.Id).HasName("pk_exits");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.ExitPrice).HasPrecision(18, 6);
        builder.Property(x => x.ExitReason).HasMaxLength(32).IsRequired();
        builder.Property(x => x.GrossPnl).HasPrecision(18, 2);
        builder.Property(x => x.Charges).HasPrecision(18, 2);
        builder.Property(x => x.NetPnl).HasPrecision(18, 2);
        builder.Property(x => x.ChargeSheetId).HasMaxLength(100);
        builder.Property(x => x.CostBasis).HasMaxLength(120).IsRequired();
        builder.Property(x => x.RBasis).HasMaxLength(5).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => x.EntryId).IsUnique().HasDatabaseName("uq_exits_entry_id");
        builder.HasOne<PaperEntryRecord>().WithMany().HasForeignKey(x => x.EntryId).HasConstraintName("fk_exits_entries_entry_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.skips</c>: a final fact about an armed allocation with no entry, one per ticket and account.</summary>
internal sealed class PaperSkipRecordConfiguration : IEntityTypeConfiguration<PaperSkipRecord>
{
    public void Configure(EntityTypeBuilder<PaperSkipRecord> builder)
    {
        builder.ToTable("skips", Schemas.Paper, t => t.HasCheckConstraint("ck_skips_shape", "code IN (" + string.Join(", ", SkipCodes.All.Select(c => "'" + c + "'")) + ") AND length(btrim(detail)) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_skips");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.PlanItemId, x.AccountId }).IsUnique().HasDatabaseName("uq_skips_item_account");
        builder.HasOne<PaperEpochRecord>().WithMany().HasForeignKey(x => x.EpochId).HasConstraintName("fk_skips_epochs_epoch_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.capture_runs</c>: one row per session a run processed, so coverage can be read from this table alone.</summary>
internal sealed class PaperCaptureRunRecordConfiguration : IEntityTypeConfiguration<PaperCaptureRunRecord>
{
    public void Configure(EntityTypeBuilder<PaperCaptureRunRecord> builder)
    {
        builder.ToTable("capture_runs", Schemas.Paper, t => t.HasCheckConstraint("ck_capture_runs_shape", "outcome IN ('done', 'partial', 'failed') AND seen >= 0 AND pending >= 0 AND integrity >= 0 AND finished_at >= started_at"));
        builder.HasKey(x => x.Id).HasName("pk_capture_runs");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Outcome).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.SessionDate, x.Id }).HasDatabaseName("ix_capture_runs_session_date_id");
    }
}
