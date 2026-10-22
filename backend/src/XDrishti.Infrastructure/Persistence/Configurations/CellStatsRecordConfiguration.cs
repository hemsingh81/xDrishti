using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.cell_stats</c> (spec P6-01, ADR 0064): one row per cell, window and snapshot; a snapshot is the statistics as of one date from named stored runs. Insert-only for the runtime role. A plain table: a few hundred rows per snapshot. No foreign key
/// to the runs: the sources are named in text, so the snapshot stays readable whatever happens to the run tables.
/// </summary>
internal sealed class CellStatsRecordConfiguration : IEntityTypeConfiguration<CellStatsRecord>
{
    public void Configure(EntityTypeBuilder<CellStatsRecord> builder)
    {
        builder.ToTable("cell_stats", Schemas.Research, t => t.HasCheckConstraint("ck_cell_stats_shape", "snapshot_key ~ '^[0-9a-f]{64}$' AND window_code IN ('all','250d','60d') AND n >= 0 AND wins >= 0 AND wins <= n"));
        builder.HasKey(x => x.Id).HasName("pk_cell_stats");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.SnapshotKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.AsOf).HasColumnType("date").IsRequired();
        builder.Property(x => x.CellKey).HasColumnType("text").IsRequired();
        builder.Property(x => x.Strategy).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Timeframe).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Side).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Style).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExitPolicy).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Regime).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Window).HasColumnName("window_code").HasMaxLength(8).IsRequired();
        builder.Property(x => x.Sources).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.SnapshotKey, x.CellKey, x.Window }).IsUnique().HasDatabaseName("uq_cell_stats_snapshot_cell_window");
        builder.HasIndex(x => new { x.AsOf, x.CreatedAt }).HasDatabaseName("ix_cell_stats_as_of_created");
        builder.HasIndex(x => x.CellKey).HasDatabaseName("ix_cell_stats_cell_key");
    }
}
