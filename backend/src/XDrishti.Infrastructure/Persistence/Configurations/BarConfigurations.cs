using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Bars;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.bars_1m</c>: one row per instrument and minute, raw prices. The migration turns it into a TimescaleDB hypertable (1-month chunks on the bar start)
/// compressed after 7 days. No foreign key to the instrument list: 40 million rows are checked on every insert otherwise, and the instrument ids come from the list.
/// </summary>
internal sealed class Bar1mConfiguration : IEntityTypeConfiguration<Bar1mRecord>
{
    public void Configure(EntityTypeBuilder<Bar1mRecord> builder)
    {
        builder.ToTable("bars_1m", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_bars_1m_ohlc", "low > 0 AND low <= open AND low <= close AND high >= open AND high >= close AND high >= low");
            t.HasCheckConstraint("ck_bars_1m_volume", "volume >= 0");
            t.HasCheckConstraint("ck_bars_1m_source", "source IN ('csv', 'dhan')");
        });
        builder.HasKey(x => new { x.InstrumentId, x.Ts });
        builder.Property(x => x.Source).HasMaxLength(4).IsRequired();
    }
}

/// <summary>
/// <c>market.bars_agg</c>: the derived timeframes, keyed by instrument, timeframe and bucket start (UTC instant; the day, week and month buckets start at India
/// midnight). <c>bars</c> is the number of 1-minute bars in the bucket, <c>complete</c> says none was missing.
/// </summary>
internal sealed class BarAggConfiguration : IEntityTypeConfiguration<BarAggRecord>
{
    public void Configure(EntityTypeBuilder<BarAggRecord> builder)
    {
        builder.ToTable("bars_agg", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_bars_agg_ohlc", "low > 0 AND low <= open AND low <= close AND high >= open AND high >= close AND high >= low");
            t.HasCheckConstraint("ck_bars_agg_volume", "volume >= 0");
            t.HasCheckConstraint("ck_bars_agg_bars", "bars > 0");
            t.HasCheckConstraint("ck_bars_agg_timeframe", "timeframe IN ('5m', '10m', '15m', '30m', '1h', '1D', '1W', '1M')");
        });
        builder.HasKey(x => new { x.InstrumentId, x.Timeframe, x.Ts });
        builder.Property(x => x.Timeframe).HasMaxLength(3).IsRequired();
    }
}

internal sealed class BarCoverageConfiguration : IEntityTypeConfiguration<BarCoverageRecord>
{
    public void Configure(EntityTypeBuilder<BarCoverageRecord> builder)
    {
        builder.ToTable("bar_coverage", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_bar_coverage_range", "first_bar <= last_bar AND bars > 0");
            t.HasCheckConstraint("ck_bar_coverage_dirty", "(dirty_from IS NULL) = (dirty_to IS NULL) AND (dirty_from IS NULL OR dirty_from <= dirty_to)");
        });
        builder.HasKey(x => x.InstrumentId);
        builder.Property(x => x.InstrumentId).ValueGeneratedNever();
        builder.HasOne<InstrumentRecord>().WithOne().HasForeignKey<BarCoverageRecord>(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Aggregates).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();

        // "How far behind is everything" looks at the newest bar; "what needs aggregating" at the changed ranges.
        builder.HasIndex(x => x.LastBar).HasDatabaseName("ix_bar_coverage_last_bar");
        builder.HasIndex(x => x.DirtyFrom).HasDatabaseName("ix_bar_coverage_dirty").HasFilter("dirty_from IS NOT NULL");
    }
}

internal sealed class CsvFileConfiguration : IEntityTypeConfiguration<CsvFileRecord>
{
    public void Configure(EntityTypeBuilder<CsvFileRecord> builder)
    {
        builder.ToTable("csv_files", Schemas.Market, t => t.HasCheckConstraint("ck_csv_files_counts", "rows >= 0 AND inserted >= 0 AND updated >= 0 AND repaired >= 0 AND rejected >= 0 AND size_bytes >= 0"));
        builder.HasKey(x => x.FileName);
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.HasOne<InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
