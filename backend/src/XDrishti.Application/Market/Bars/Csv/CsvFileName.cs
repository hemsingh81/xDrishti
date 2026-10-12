using System.Text.RegularExpressions;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Bars.Csv;

/// <summary>The names of the files of the CSV folder: <c>&lt;SYMBOL&gt;_1m.csv</c> or <c>&lt;SYMBOL&gt;_1m.csv.gz</c>, the symbol being letters, digits, <c>&amp;</c> and <c>-</c> (ADR 0016 §5).</summary>
internal static partial class CsvFileName
{
    [GeneratedRegex(@"^(?<symbol>[A-Za-z0-9&-]+)_1m\.csv(\.gz)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();

    /// <summary>The symbol of a matching file name (as written, case kept).</summary>
    public static bool TryParse(string name, out string symbol)
    {
        var match = Pattern().Match(name);
        symbol = match.Success ? match.Groups["symbol"].Value : string.Empty;
        return match.Success;
    }

    /// <summary>The master-list symbol the file belongs to: <c>NSE:&lt;SYMBOL&gt;</c>.</summary>
    public static string Canonical(string symbol) => CanonicalSymbol.ForListed(symbol);

    public static bool IsGzip(string name) => name.EndsWith(".gz", StringComparison.Ordinal);
}
