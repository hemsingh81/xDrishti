using System.Globalization;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

internal sealed class DataSettings(IOptions<DataOptions> options) : IDataSettings
{
    public string CsvFolder => options.Value.CsvFolder;

    public int DhanInitialDays => options.Value.Dhan.InitialDays;

    public int DhanInitialDaysIndex => options.Value.Dhan.InitialDaysIndex;

    public int DhanRequestsPerSecond => options.Value.Dhan.RequestsPerSecond;

    public int CsvMaxRowsPerFile => options.Value.Csv.MaxRowsPerFile;

    public long CsvMaxBytesPerFile => options.Value.Csv.MaxBytesPerFile;

    public int CsvMaxSpanDays => options.Value.Csv.MaxSpanDays;

    public int GapLookbackSessions => options.Value.Gaps.LookbackSessions;

    public bool EodEnabled => options.Value.Eod.Enabled;

    public TimeOnly EodTimeIst => TimeOnly.ParseExact(options.Value.Eod.TimeIst, EodDataOptions.TimeFormat, CultureInfo.InvariantCulture);

    public int QualitySessions => options.Value.Quality.Sessions;

    public double QualitySpikeMultiple => options.Value.Quality.SpikeMultiple;

    public double QualityMaxMissingPercent => options.Value.Quality.MaxMissingPercent;

    public double QualityMaxFailedPercent => options.Value.Quality.MaxFailedPercent;
}
