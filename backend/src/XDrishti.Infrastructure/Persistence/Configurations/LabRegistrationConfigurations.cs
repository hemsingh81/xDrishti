using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>lab.registrations</c> (CF-06, ADR 0207): the frozen pre-registration of a strategy version, one row per version. Insert-only for the runtime role. No foreign key to the strategy tables: the record must outlive any change to them.</summary>
internal sealed class RegistrationRecordConfiguration : IEntityTypeConfiguration<RegistrationRecord>
{
    public void Configure(EntityTypeBuilder<RegistrationRecord> builder)
    {
        builder.ToTable("registrations", Schemas.Lab, t => t.HasCheckConstraint("ck_registrations_shape", "version >= 1 AND text_hash ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(x => x.Id).HasName("pk_registrations");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.Rationale).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TextHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.Actor).HasColumnType("text").IsRequired();
        builder.HasIndex(x => new { x.StrategyId, x.Version }).IsUnique().HasDatabaseName("uq_registrations_version");
    }
}

/// <summary><c>lab.plan_freezes</c> (CF-06, ADR 0207): the plan hash of the first full or robust run of a version and of each later changed plan. Insert-only for the runtime role.</summary>
internal sealed class PlanFreezeRecordConfiguration : IEntityTypeConfiguration<PlanFreezeRecord>
{
    public void Configure(EntityTypeBuilder<PlanFreezeRecord> builder)
    {
        builder.ToTable("plan_freezes", Schemas.Lab, t => t.HasCheckConstraint("ck_plan_freezes_shape", "version >= 1 AND plan_hash ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(x => x.Id).HasName("pk_plan_freezes");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.PlanHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.FirstRunId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.StrategyId, x.Version, x.PlanHash }).IsUnique().HasDatabaseName("uq_plan_freezes_plan");
    }
}

/// <summary><c>lab.holdout_blocks</c> (CF-06, ADR 0207): the locked block, fixed once. A later block supersedes it; the older one is then development and burned. Insert-only for the runtime role.</summary>
internal sealed class HoldOutBlockRecordConfiguration : IEntityTypeConfiguration<HoldOutBlockRecord>
{
    public void Configure(EntityTypeBuilder<HoldOutBlockRecord> builder)
    {
        builder.ToTable("holdout_blocks", Schemas.Lab, t => t.HasCheckConstraint("ck_holdout_blocks_shape", "history_first < start_date AND start_date <= history_last AND length_months >= 1 AND setting_hash ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(x => x.Id).HasName("pk_holdout_blocks");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StartDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.HistoryFirst).HasColumnType("date").IsRequired();
        builder.Property(x => x.HistoryLast).HasColumnType("date").IsRequired();
        builder.Property(x => x.LengthMonths).IsRequired();
        builder.Property(x => x.SettingHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.StartDate).IsUnique().HasDatabaseName("uq_holdout_blocks_start");
        builder.Property(x => x.PrevStart).HasColumnType("date");
        builder.Property(x => x.Orphan).HasDefaultValue(false).IsRequired();
        // HO-01: a block has at most one successor, and the chain has one first block (every non-orphan row has orphan = false, so a unique index on it, filtered to the first blocks, admits one row)
        builder.HasIndex(x => x.PrevStart).IsUnique().HasDatabaseName("uq_holdout_blocks_prev");
        builder.HasIndex(x => x.Orphan).IsUnique().HasFilter("prev_start IS NULL AND NOT orphan").HasDatabaseName("uq_holdout_blocks_first");
    }
}

/// <summary><c>lab.holdout_releases</c> (HO-01, ADR 0283): the release of a block, owner or auto, with the setting snapshot and the next block it opened. Insert-only for the runtime role; never changed.</summary>
internal sealed class HoldOutReleaseRecordConfiguration : IEntityTypeConfiguration<HoldOutReleaseRecord>
{
    public void Configure(EntityTypeBuilder<HoldOutReleaseRecord> builder)
    {
        builder.ToTable("holdout_releases", Schemas.Lab, t => t.HasCheckConstraint("ck_holdout_releases_shape", "mode IN ('owner','auto') AND setting_hash ~ '^[0-9a-f]{64}$' AND next_block_start > block_start AND length(actor) > 0"));
        builder.HasKey(x => x.BlockStart).HasName("pk_holdout_releases");
        builder.Property(x => x.BlockStart).HasColumnType("date");
        builder.Property(x => x.ReleasedAt).IsRequired();
        builder.Property(x => x.Mode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Actor).HasColumnType("text").IsRequired();
        builder.Property(x => x.SettingHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Looked).IsRequired();
        builder.Property(x => x.NextBlockStart).HasColumnType("date").IsRequired();
    }
}

/// <summary><c>lab.holdout_unlocks</c> (CF-06, ADR 0207): one look at the hold-out per version and kind, irreversible. Insert-only for the runtime role.</summary>
internal sealed class HoldOutUnlockRecordConfiguration : IEntityTypeConfiguration<HoldOutUnlockRecord>
{
    public void Configure(EntityTypeBuilder<HoldOutUnlockRecord> builder)
    {
        builder.ToTable("holdout_unlocks", Schemas.Lab, t => t.HasCheckConstraint("ck_holdout_unlocks_shape", "version >= 1 AND kind IN ('strategy','model') AND mode IN ('owner','auto') AND setting_hash ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(x => x.Id).HasName("pk_holdout_unlocks");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.Kind).HasColumnType("text").IsRequired();
        builder.Property(x => x.UnlockedAt).IsRequired();
        builder.Property(x => x.Mode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Actor).HasColumnType("text").IsRequired();
        builder.Property(x => x.SettingHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ReusedWindow).IsRequired();
        builder.Property(x => x.BlockStart).HasColumnType("date").IsRequired();
        builder.Property(x => x.LabRunId).IsRequired();
        builder.Property(x => x.Result).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.StrategyId, x.Version, x.Kind }).IsUnique().HasDatabaseName("uq_holdout_unlocks_version");
        builder.HasIndex(x => x.BlockStart, "ix_holdout_unlocks_block").HasDatabaseName("ix_holdout_unlocks_block");   // a named index: without the name it is the same index as the unique one below and the later call wins
        // at most one fresh look at a block, decided by the database and not by a read before the insert (two looks at once cannot both be fresh)
        builder.HasIndex(x => x.BlockStart).IsUnique().HasFilter("NOT reused_window").HasDatabaseName("uq_holdout_unlocks_fresh");
    }
}
