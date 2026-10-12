using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>The market data pipeline (configuration section <c>Data</c>).</summary>
internal sealed class DataOptions
{
    public const string SectionName = "Data";

    /// <summary>The folder with the <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c> files. In the container it is the read-only mount <c>/data/csv</c>.</summary>
    [Required]
    public string CsvFolder { get; set; } = "/data/csv";

    [Required]
    public DhanDataOptions Dhan { get; set; } = new();

    /// <summary>The limits of one CSV file.</summary>
    [Required]
    public CsvDataOptions Csv { get; set; } = new();

    public const string InvalidMessage = "Data:Dhan:InitialDays must be 1 to 3650 and Data:Dhan:RequestsPerSecond 1 to 5; Data:Csv:MaxRowsPerFile must be 1000 to 100000000, Data:Csv:MaxBytesPerFile 1048576 to 17179869184 and Data:Csv:MaxSpanDays 31 to 36500.";

    /// <summary>The settings of the nested section are checked here: the data-annotation validation does not look into nested objects.</summary>
    public static bool IsValid(DataOptions options) =>
        options.Dhan is { InitialDays: >= 1 and <= 3650, RequestsPerSecond: >= 1 and <= 5 }
        && options.Csv is { MaxRowsPerFile: >= 1000 and <= 100_000_000, MaxBytesPerFile: >= 1L << 20 and <= 1L << 34, MaxSpanDays: >= 31 and <= 36_500 };
}

/// <summary>The fetch of 1-minute bars from Dhan's Data API (<c>Data:Dhan</c>).</summary>
internal sealed class DhanDataOptions
{
    /// <summary>How many days back the first fetch of an instrument goes.</summary>
    [Range(1, 3650)]
    public int InitialDays { get; set; } = 365;

    /// <summary>The most requests per second sent to the Data API (Dhan allows 5).</summary>
    [Range(1, 5)]
    public int RequestsPerSecond { get; set; } = 4;
}

/// <summary>What bounds the reading of one CSV file (<c>Data:Csv</c>); a file over a limit fails as a whole, with the reason in the job.</summary>
internal sealed class CsvDataOptions
{
    /// <summary>The most data rows of one file (the real files have about 200 000).</summary>
    [Range(1000, 100_000_000)]
    public int MaxRowsPerFile { get; set; } = 5_000_000;

    /// <summary>The most bytes read from one file after decompression (a compressed file can be a thousand times larger than it is).</summary>
    [Range(1L << 20, 1L << 34)]
    public long MaxBytesPerFile { get; set; } = 1L << 30;

    /// <summary>The longest time one file may span; each month of it is a chunk of the bars table (3 700 days are about 120 chunks).</summary>
    [Range(31, 36_500)]
    public int MaxSpanDays { get; set; } = 3_700;
}
