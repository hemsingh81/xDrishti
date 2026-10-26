namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>Configuration of the market data pipeline (<c>Data:*</c>).</summary>
public interface IDataSettings
{
    /// <summary>The folder with the <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c> files (<c>Data:CsvFolder</c>, in the container <c>/data/csv</c>).</summary>
    string CsvFolder { get; }

    /// <summary>How many days of 1-minute bars are fetched from Dhan for an instrument that has none (<c>Data:Dhan:InitialDays</c>, default 365).</summary>
    int DhanInitialDays { get; }

    /// <summary>How many days of 1-minute bars are fetched from Dhan for an instrument of type index that has none (<c>Data:Dhan:InitialDaysIndex</c>, default 1825: the index history behind the market context, P3-01d).</summary>
    int DhanInitialDaysIndex { get; }

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

    /// <summary>First-start setting of the end-of-day service (ADR 0018): whether it starts switched on (<c>Data:Eod:Enabled</c>, default true). Read once, when the service has no stored row; the Services screen decides after that.</summary>
    bool EodEnabled { get; }

    /// <summary>First-start setting of the end-of-day service (ADR 0018): time of day, India time, of the run (<c>Data:Eod:TimeIst</c>, <c>HH:mm</c>, default 15:50). Read once, when the service has no stored row.</summary>
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
