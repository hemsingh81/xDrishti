namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>Configuration of the market data pipeline (<c>Data:*</c>).</summary>
public interface IDataSettings
{
    /// <summary>The folder with the <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c> files (<c>Data:CsvFolder</c>, in the container <c>/data/csv</c>).</summary>
    string CsvFolder { get; }

    /// <summary>How many days of 1-minute bars are fetched from Dhan for an instrument that has none (<c>Data:Dhan:InitialDays</c>, default 365).</summary>
    int DhanInitialDays { get; }

    /// <summary>The most Dhan Data API requests per second (<c>Data:Dhan:RequestsPerSecond</c>, default 4).</summary>
    int DhanRequestsPerSecond { get; }

    /// <summary>The most data rows one CSV file may have (<c>Data:Csv:MaxRowsPerFile</c>, default 5 000 000); a larger file fails.</summary>
    int CsvMaxRowsPerFile { get; }

    /// <summary>The most bytes read from one CSV file after decompression (<c>Data:Csv:MaxBytesPerFile</c>, default 1 GiB); a larger file fails.</summary>
    long CsvMaxBytesPerFile { get; }

    /// <summary>The longest time one CSV file may span (<c>Data:Csv:MaxSpanDays</c>, default 3 700, about 120 monthly chunks); a longer one fails.</summary>
    int CsvMaxSpanDays { get; }

    /// <summary>How many of the latest trading sessions are checked for missing bars (<c>Data:Gaps:LookbackSessions</c>, default 30).</summary>
    int GapLookbackSessions { get; }

    /// <summary>Whether the end-of-day run starts by itself on trading days (<c>Data:Eod:Enabled</c>, default true).</summary>
    bool EodEnabled { get; }

    /// <summary>Time of day, India time, of the end-of-day run (<c>Data:Eod:TimeIst</c>, <c>HH:mm</c>, default 15:50).</summary>
    TimeOnly EodTimeIst { get; }

    /// <summary>How many of the latest closed trading sessions the data quality checks look at (<c>Data:Quality:Sessions</c>, default 5).</summary>
    int QualitySessions { get; }

    /// <summary>A 1-minute move needs this many times the median move (and more than 1 %) to be a spike (<c>Data:Quality:SpikeMultiple</c>, default 20).</summary>
    double QualitySpikeMultiple { get; }

    /// <summary>More than this percentage of the expected minutes missing is a warning (<c>Data:Quality:MaxMissingPercent</c>, default 1).</summary>
    double QualityMaxMissingPercent { get; }

    /// <summary>The gate blocks when more than this percentage of the tracked instruments fail the checks (<c>Data:Quality:MaxFailedPercent</c>, default 5).</summary>
    double QualityMaxFailedPercent { get; }
}
