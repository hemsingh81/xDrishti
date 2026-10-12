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
}
