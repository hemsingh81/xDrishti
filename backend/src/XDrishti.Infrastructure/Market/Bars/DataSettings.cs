using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

internal sealed class DataSettings(IOptions<DataOptions> options) : IDataSettings
{
    public string CsvFolder => options.Value.CsvFolder;

    public int DhanInitialDays => options.Value.Dhan.InitialDays;

    public int DhanRequestsPerSecond => options.Value.Dhan.RequestsPerSecond;

    public int CsvMaxRowsPerFile => options.Value.Csv.MaxRowsPerFile;

    public long CsvMaxBytesPerFile => options.Value.Csv.MaxBytesPerFile;

    public int CsvMaxSpanDays => options.Value.Csv.MaxSpanDays;
}
