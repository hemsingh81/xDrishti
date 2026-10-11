using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Import;

/// <summary>NSE's Nifty Total Market list: the industry of each ISIN (ADR 0010).</summary>
internal sealed class SectorList(IReadOnlyDictionary<string, string> industryByIsin)
{
    public const string Source = "nse_total_market";

    public const string FailureCode = "instruments.sector_source_invalid";

    private static readonly string[] Columns = ["Company Name", "Industry", "Symbol", "Series", "ISIN Code"];

    public int Count => industryByIsin.Count;

    public string? IndustryOf(string? isin) => isin is not null && industryByIsin.TryGetValue(isin, out var industry) ? industry : null;

    public static async ValueTask<Result<SectorList>> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var text = new StreamReader(content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var csv = new CsvRecordReader(text);
        try
        {
            var headerLine = await csv.ReadLineAsync(cancellationToken);
            var header = headerLine is null ? [] : (await csv.SplitAsync(headerLine, cancellationToken)).Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();
            if (!Columns.All(c => header.Contains(c, StringComparer.Ordinal)))
            {
                return Result.Failure<SectorList>(Error.Upstream(FailureCode, "The NSE sector list has changed shape (an expected column is missing)."));
            }

            var industryAt = Array.IndexOf(header, "Industry");
            var isinAt = Array.IndexOf(header, "ISIN Code");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            while (await csv.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var fields = await csv.SplitAsync(line, cancellationToken);
                if (fields.Length != header.Length)
                {
                    continue;
                }

                var isin = fields[isinAt].Trim();
                var industry = fields[industryAt].Trim();
                if (isin.Length > 0 && industry.Length > 0)
                {
                    map.TryAdd(isin, industry);
                }
            }

            return map.Count == 0
                ? Result.Failure<SectorList>(Error.Upstream(FailureCode, "The NSE sector list is empty."))
                : Result.Success(new SectorList(map));
        }
        catch (FormatException)
        {
            return Result.Failure<SectorList>(Error.Upstream(FailureCode, "The NSE sector list is malformed."));
        }
    }
}
