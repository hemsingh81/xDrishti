using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>lab.test_ledger</c> (spec P5-03, ADR 0059): one row per Lab run created, so every look at a strategy family is counted. Insert-only for the runtime role; <c>lab_run_id</c> is unique, which makes the append idempotent. No foreign key to
/// <c>lab_runs</c>: the ledger must outlive any change to the run table.
/// </summary>
internal sealed class TestLedgerRowConfiguration : IEntityTypeConfiguration<TestLedgerRow>
{
    public void Configure(EntityTypeBuilder<TestLedgerRow> builder)
    {
        builder.ToTable("test_ledger", Schemas.Lab, t => t.HasCheckConstraint("ck_test_ledger_hash", "strategy_hash ~ '^[0-9a-f]{64}$' AND window_from <= window_to"));
        builder.HasKey(x => x.Id).HasName("pk_test_ledger");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.StrategyHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ParentHash).HasColumnType("character(64)");
        builder.Property(x => x.RunKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.UniverseLabel).HasColumnType("text").IsRequired();
        builder.Property(x => x.WindowFrom).HasColumnType("date").IsRequired();
        builder.Property(x => x.WindowTo).HasColumnType("date").IsRequired();
        builder.Property(x => x.Method).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.LabRunId).IsUnique().HasDatabaseName("uq_test_ledger_run");
        builder.HasIndex(x => new { x.StrategyId, x.CreatedAt }).HasDatabaseName("ix_test_ledger_strategy_created");
    }
}
