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

    /// <summary>The check for missing bars (ADR 0017 §3).</summary>
    [Required]
    public GapDataOptions Gaps { get; set; } = new();

    /// <summary>The scheduled end-of-day run (ADR 0017 §2).</summary>
    [Required]
    public EodDataOptions Eod { get; set; } = new();

    /// <summary>The data quality checks (ADR 0017 §4).</summary>
    [Required]
    public QualityDataOptions Quality { get; set; } = new();

    public const string InvalidMessage = "Data:Dhan:InitialDays must be 1 to 3650 and Data:Dhan:RequestsPerSecond 1 to 5; Data:Csv:MaxRowsPerFile must be 1000 to 100000000, Data:Csv:MaxBytesPerFile 1048576 to 17179869184 and Data:Csv:MaxSpanDays 31 to 36500; Data:Gaps:LookbackSessions must be 1 to 250; Data:Eod:TimeIst must be a time of day as HH:mm (24-hour), India time, e.g. 15:50; Data:Quality:Sessions must be 1 to 60, Data:Quality:SpikeMultiple 2 to 1000, Data:Quality:MaxMissingPercent 0 to 100 and Data:Quality:MaxFailedPercent 0 to 100.";

    /// <summary>The settings of the nested section are checked here: the data-annotation validation does not look into nested objects.</summary>
    public static bool IsValid(DataOptions options) =>
        options.Dhan is { InitialDays: >= 1 and <= 3650, RequestsPerSecond: >= 1 and <= 5 }
        && options.Csv is { MaxRowsPerFile: >= 1000 and <= 100_000_000, MaxBytesPerFile: >= 1L << 20 and <= 1L << 34, MaxSpanDays: >= 31 and <= 36_500 }
        && options.Gaps is { LookbackSessions: >= 1 and <= 250 }
        && options.Eod is { } eod && EodDataOptions.IsValidTime(eod.TimeIst)
        && options.Quality is { Sessions: >= 1 and <= 60, SpikeMultiple: >= 2 and <= 1000, MaxMissingPercent: >= 0 and <= 100, MaxFailedPercent: >= 0 and <= 100 };
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

/// <summary>The search for missing 1-minute bars (<c>Data:Gaps</c>).</summary>
internal sealed class GapDataOptions
{
    /// <summary>How many of the latest trading sessions are checked (a gap older than that is not looked for, but one on record is retried until it is filled or unfillable).</summary>
    [Range(1, 250)]
    public int LookbackSessions { get; set; } = 30;
}

/// <summary>The end-of-day run that starts by itself on trading days (<c>Data:Eod</c>).</summary>
internal sealed class EodDataOptions
{
    public const string TimeFormat = "HH:mm";

    /// <summary>Whether the API starts the update by itself on trading days.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Time of day, India time (UTC+05:30), as <c>HH:mm</c>; never before the session has closed (15:30).</summary>
    [Required]
    public string TimeIst { get; set; } = "15:50";

    public static bool IsValidTime(string? value) =>
        TimeOnly.TryParseExact(value, TimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
}

/// <summary>The data quality checks (<c>Data:Quality</c>): the window and the thresholds of the verdicts.</summary>
internal sealed class QualityDataOptions
{
    /// <summary>How many of the latest closed trading sessions are checked.</summary>
    [Range(1, 60)]
    public int Sessions { get; set; } = 5;

    /// <summary>A 1-minute move needs this many times the median move (and more than 1 %) to be a spike.</summary>
    [Range(2.0, 1000.0)]
    public double SpikeMultiple { get; set; } = 20;

    /// <summary>More than this percentage of the expected minutes missing is a warning.</summary>
    [Range(0.0, 100.0)]
    public double MaxMissingPercent { get; set; } = 1;

    /// <summary>The gate blocks when more than this percentage of the tracked instruments fail.</summary>
    [Range(0.0, 100.0)]
    public double MaxFailedPercent { get; set; } = 5;
}
