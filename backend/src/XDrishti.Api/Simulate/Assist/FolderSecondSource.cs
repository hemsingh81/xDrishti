using System.Globalization;
using System.Text;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>
/// The offline second source (spec P3-09c D2): one file per symbol and date in a folder the owner fills, for example from a TradingView export, named <c>&lt;TICKER&gt;_&lt;yyyy-MM-dd&gt;.csv</c> (see <see cref="AssistBarsCsv.ParseVendor"/> for the columns).
/// No network and no allow-list change. A link, an oversized file or a file that cannot be read is "unavailable" for that day.
/// </summary>
internal sealed class FolderSecondSource(string folder) : ISecondSource
{
    public string Name => "folder";

    public async ValueTask<SecondDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
    {
        var ticker = symbol[(symbol.IndexOf(':', StringComparison.Ordinal) + 1)..];
        if (ticker.Length == 0 || !ticker.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '&' or '_'))
        {
            return SecondDay.Missing("the symbol cannot be a file name");
        }

        var file = Path.Combine(folder, ticker + "_" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
        try
        {
            if (!File.Exists(file) || ExportPaths.IsLink(file))
            {
                return SecondDay.Missing("no file " + Path.GetFileName(file));
            }

            if (new FileInfo(file).Length > AssistBarsCsv.MaxBytes)
            {
                return SecondDay.Missing("the file is too large");
            }

            var text = new UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(file, cancellationToken));
            var day = AssistBarsCsv.ParseVendor(text) is { } all ? AssistBarsCsv.OfDate(all, date) : null;
            return day is { Count: > 0 } ? new SecondDay(day, null) : SecondDay.Missing("the file has no usable bars of the day");
        }
        catch (Exception bad) when (bad is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return SecondDay.Missing("the file cannot be read");
        }
    }
}
