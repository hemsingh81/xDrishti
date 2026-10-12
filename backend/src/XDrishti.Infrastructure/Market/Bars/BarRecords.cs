namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>A row of <c>market.bars_1m</c> (a TimescaleDB hypertable). Persistence shape only: bars are written by <see cref="BarStore"/> with binary COPY and read by projection.</summary>
internal sealed class Bar1mRecord
{
    public Guid InstrumentId { get; set; }

    public DateTimeOffset Ts { get; set; }

    public double Open { get; set; }

    public double High { get; set; }

    public double Low { get; set; }

    public double Close { get; set; }

    public long Volume { get; set; }

    /// <summary>csv · dhan.</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>A row of <c>market.bars_agg</c>: one bar of a derived timeframe (5m … 1M). Written only by <see cref="BarAggregates"/>.</summary>
internal sealed class BarAggRecord
{
    public Guid InstrumentId { get; set; }

    public string Timeframe { get; set; } = string.Empty;

    public DateTimeOffset Ts { get; set; }

    public double Open { get; set; }

    public double High { get; set; }

    public double Low { get; set; }

    public double Close { get; set; }

    public long Volume { get; set; }

    public int Bars { get; set; }

    public bool Complete { get; set; }
}

/// <summary>
/// A row of <c>market.bar_coverage</c>: what is stored for an instrument, kept up to date by every write of bars. <see cref="DirtyFrom"/> and <see cref="DirtyTo"/>
/// are the range of bar starts that changed since the aggregates were built (null when they are current); <see cref="Aggregates"/> is a JSON object
/// <c>{"5m": {"bars": n, "last": epochSeconds}, …}</c> written when they are built.
/// </summary>
internal sealed class BarCoverageRecord
{
    public Guid InstrumentId { get; set; }

    public DateTimeOffset FirstBar { get; set; }

    public DateTimeOffset LastBar { get; set; }

    public long Bars { get; set; }

    public long CsvBars { get; set; }

    public long DhanBars { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DirtyFrom { get; set; }

    public DateTimeOffset? DirtyTo { get; set; }

    public DateTimeOffset? AggregatesBuiltAt { get; set; }

    public string Aggregates { get; set; } = "{}";
}

/// <summary>A row of <c>market.csv_files</c>: a CSV file that was loaded, as it looked then (size, modified time) and what loading it did.</summary>
internal sealed class CsvFileRecord
{
    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public Guid? InstrumentId { get; set; }

    public long Rows { get; set; }

    public long Inserted { get; set; }

    public long Updated { get; set; }

    public long Repaired { get; set; }

    public long Rejected { get; set; }

    public DateTimeOffset LoadedAt { get; set; }

    /// <summary>The first and last bar start of the file's accepted rows: the range an undo of the load works on. Null for a file loaded before they were recorded.</summary>
    public DateTimeOffset? FirstBar { get; set; }

    public DateTimeOffset? LastBar { get; set; }

    /// <summary>When the load of this file was undone (ADR 0017 §6); null while it is loaded. An undone file is pending again; loading it again clears this.</summary>
    public DateTimeOffset? UndoneAt { get; set; }
}
