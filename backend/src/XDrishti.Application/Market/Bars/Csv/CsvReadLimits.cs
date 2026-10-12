using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Market.Bars.Csv;

/// <summary>
/// What bounds the reading of one CSV file so that a wrong or hostile file cannot run for ever or fill the database: the data rows, the bytes read (after
/// decompression: a compressed file can be a thousand times larger than it is), the time one file may span (each month of it is a chunk of the hypertable) and the
/// latest time that is a bar and not a mistake. A file that breaks one of them fails as a whole.
/// </summary>
internal sealed record CsvReadLimits(long MaxRows, long MaxBytes, TimeSpan MaxSpan, long LatestEpochSeconds)
{
    /// <summary>The defaults of <c>Data:Csv</c> (5 million rows, 1 GiB, 3 700 days) and no limit on the future other than the year 2100.</summary>
    public static CsvReadLimits Default { get; } = new(5_000_000, 1L << 30, TimeSpan.FromDays(3_700), long.MaxValue);

    /// <summary>The configured limits; a bar later than one day after <paramref name="now"/> is in the future.</summary>
    public static CsvReadLimits From(IDataSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new CsvReadLimits(settings.CsvMaxRowsPerFile, settings.CsvMaxBytesPerFile, TimeSpan.FromDays(settings.CsvMaxSpanDays), now.AddDays(1).ToUnixTimeSeconds());
    }
}
