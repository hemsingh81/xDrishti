using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Paper;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>paper.shadow_entries</c> (spec CF-08, ADR 0211): every candidate of a nightly plan, once per session and trade key. Insert-only for the runtime role. The composite key onto the plan item, the database clock and the before-the-open guard are added in the migration.
/// </summary>
internal sealed class ShadowEntryRecordConfiguration : IEntityTypeConfiguration<ShadowEntryRecord>
{
    public void Configure(EntityTypeBuilder<ShadowEntryRecord> builder)
    {
        builder.ToTable("shadow_entries", Schemas.Paper, t => t.HasCheckConstraint(
            "ck_shadow_entries_shape",
            "ticket_group IN ('primary', 'reserve', 'rejected') AND side IN ('BUY', 'SELL') AND row_hash ~ '^[0-9a-f]{64}$' AND (config_hash IS NULL OR config_hash ~ '^[0-9a-f]{64}$') AND (p_win IS NULL OR (p_win >= 0 AND p_win <= 1)) AND ((features IS NULL) = (feature_version IS NULL)) AND length(btrim(trade_key)) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_shadow_entries");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.TradeKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.TicketGroup).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CellKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StrategyHash).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Side).HasMaxLength(4).IsRequired();
        builder.Property(x => x.EntryLimit).HasPrecision(18, 4);
        builder.Property(x => x.Stop).HasPrecision(18, 4);
        builder.Property(x => x.Target).HasPrecision(18, 4);
        builder.Property(x => x.TimeRule).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ConfigHash).HasColumnType("char(64)");
        builder.Property(x => x.ModelId).HasMaxLength(100);
        builder.Property(x => x.FeatureVersion).HasMaxLength(40);
        builder.Property(x => x.Features).HasColumnType("jsonb");
        builder.Property(x => x.WrittenAt).HasDefaultValueSql("now()");
        builder.Property(x => x.RowHash).HasColumnType("char(64)").IsRequired();
        builder.HasIndex(x => new { x.SessionDate, x.TradeKey }).IsUnique().HasDatabaseName("uq_shadow_entries_session_trade_key");
        builder.HasIndex(x => x.PlanItemId).IsUnique().HasDatabaseName("uq_shadow_entries_plan_item_id");
        builder.HasIndex(x => x.PlanId).HasDatabaseName("ix_shadow_entries_plan_id");
        builder.HasOne<ShadowSessionRecord>().WithMany().HasForeignKey(x => x.SessionDate).HasConstraintName("fk_shadow_entries_shadow_sessions_session_date").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.shadow_sessions</c>: one row per session of the chain, <c>ok</c> with its rows or a <c>gap</c>. The digest links each row to the one before. Insert-only; the chain and the cut-off are checked by a trigger in the migration.</summary>
internal sealed class ShadowSessionRecordConfiguration : IEntityTypeConfiguration<ShadowSessionRecord>
{
    public void Configure(EntityTypeBuilder<ShadowSessionRecord> builder)
    {
        builder.ToTable("shadow_sessions", Schemas.Paper, t => t.HasCheckConstraint(
            "ck_shadow_sessions_shape",
            "status IN ('ok', 'gap') AND n_rows >= 0 AND (status = 'ok' OR n_rows = 0) AND digest ~ '^[0-9a-f]{64}$' AND (prev_digest IS NULL OR prev_digest ~ '^[0-9a-f]{64}$')"));
        builder.HasKey(x => x.SessionDate).HasName("pk_shadow_sessions");
        builder.Property(x => x.Status).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Digest).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.PrevDigest).HasColumnType("char(64)");
        builder.Property(x => x.WrittenAt).HasDefaultValueSql("now()");
    }
}

/// <summary><c>paper.shadow_grades</c>: the grade of a shadow entry, one per entry, written once after the session (the grader is a later slice). Insert-only; the database stamps <c>graded_at</c> and refuses a grade before the open.</summary>
internal sealed class ShadowGradeRecordConfiguration : IEntityTypeConfiguration<ShadowGradeRecord>
{
    public void Configure(EntityTypeBuilder<ShadowGradeRecord> builder)
    {
        builder.ToTable("shadow_grades", Schemas.Paper, t =>
        {
            t.HasCheckConstraint(
            "ck_shadow_grades_shape",
            "status IN ('graded', 'not_triggered', 'not_gradable') AND ((status = 'graded') = (r_sim IS NOT NULL)) AND ((status = 'graded') = (r_basis IS NOT NULL)) AND (r_basis IS NULL OR r_basis IN ('net', 'gross')) AND (exit_at IS NULL OR entry_at IS NULL OR exit_at >= entry_at) AND length(btrim(engine_version)) > 0");
            t.HasCheckConstraint("ck_shadow_grades_gross", "(status = 'graded') = (r_gross IS NOT NULL)");
        });
        builder.HasKey(x => x.Id).HasName("pk_shadow_grades");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RBasis).HasMaxLength(5);
        builder.Property(x => x.ExitReason).HasMaxLength(32);
        builder.Property(x => x.EngineVersion).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SheetId).HasMaxLength(100);
        builder.Property(x => x.GradedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => x.EntryId).IsUnique().HasDatabaseName("uq_shadow_grades_entry_id");
        builder.HasOne<ShadowEntryRecord>().WithMany().HasForeignKey(x => x.EntryId).HasConstraintName("fk_shadow_grades_shadow_entries_entry_id").OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>paper.shadow_settings</c>: the switch <c>paper.shadow.enabled</c> as an append-only audited row; the newest row is current and no row means OFF. The seed row (actor <c>system:default</c>, ON, reason <c>ADR 0282</c>) is written once by the migration into an empty table and sealed by the stamp trigger (AM-01b).</summary>
internal sealed class ShadowSettingRecordConfiguration : IEntityTypeConfiguration<ShadowSettingRecord>
{
    public void Configure(EntityTypeBuilder<ShadowSettingRecord> builder)
    {
        builder.ToTable("shadow_settings", Schemas.Paper, t => t.HasCheckConstraint("ck_shadow_settings_shape", "(actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0) OR (actor = 'system:default' AND enabled AND reason = 'ADR 0282')"));
        builder.HasKey(x => x.Id).HasName("pk_shadow_settings");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ChangedAt).HasDefaultValueSql("now()");
    }
}

/// <summary>
/// <c>paper.evidence_sets</c> (spec AM-01 GAP-09, ADR 0282): the frozen evidence set of an edge epoch, insert-only for the runtime role; the newest row of an epoch is current. <c>edge_epoch</c> is the paper epoch id, or 0 while none exists. The trigger that refuses a row once a
/// shadow entry of the epoch exists, and the grants, are added in the migration.
/// </summary>
internal sealed class EvidenceSetRecordConfiguration : IEntityTypeConfiguration<EvidenceSetRecord>
{
    public void Configure(EntityTypeBuilder<EvidenceSetRecord> builder)
    {
        builder.ToTable("evidence_sets", Schemas.Paper, t => t.HasCheckConstraint(
            "ck_evidence_sets_shape",
            "edge_epoch >= 0 AND jsonb_typeof(versions) = 'array' AND jsonb_array_length(versions) BETWEEN 1 AND 500 AND set_hash ~ '^[0-9a-f]{64}$' AND ((mode = 'auto' AND actor = 'system:default' AND reason IS NULL) OR (mode = 'owner' AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND reason IS NOT NULL AND length(btrim(reason)) > 0) OR (mode = 'backfill' AND actor = 'system:migration' AND reason = 'AM-01b backfill: every version written before evidence sets existed; not pre-registered'))"));
        builder.HasKey(x => x.Id).HasName("pk_evidence_sets");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Versions).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SetHash).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.Mode).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.EdgeEpoch, x.Id }).HasDatabaseName("ix_evidence_sets_edge_epoch_id");
    }
}
