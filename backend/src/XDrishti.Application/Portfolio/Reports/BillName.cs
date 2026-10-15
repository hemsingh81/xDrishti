using System.Text.RegularExpressions;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// A scrip name of the global transaction report (ADR 0029): <c>Orion Gold ETF (ORIONGLD)</c> carries the ticker in brackets (the <see cref="SymbolHint"/>, a hint only: it counts when the master or the holdings have that ticker), <c>TD POWER SYSTEMS LTD.</c> does not.
/// <see cref="Key"/> is the name without the bracket reduced to its words by <see cref="DividendNames"/> (lower case, punctuation and company suffixes gone), <see cref="FullKey"/> the same for the whole text; <see cref="Display"/> is the name as written, cleaned. Names are untrusted
/// text. Pure.
/// </summary>
internal sealed partial record BillName(string Display, string Base, string? SymbolHint, string Key, string FullKey)
{
    public static BillName Parse(string? raw)
    {
        var display = ReportText.Clean(raw);
        var match = Bracket().Match(display);
        if (!match.Success)
        {
            return new BillName(display, display, null, DividendNames.Key(display), DividendNames.Key(display));
        }

        var name = match.Groups["name"].Value.Trim();
        var hint = TradeRules.NormaliseSymbol(match.Groups["symbol"].Value);
        return new BillName(display, name, hint.Length == 0 ? null : hint, DividendNames.Key(name), DividendNames.Key(display));
    }

    /// <summary>
    /// True when two name keys are two spellings of one company: the same number of words (two or more) and every word the same or — for at most one word — one a prefix of the other of at least 3 letters (<c>chem</c> and <c>chemicals</c>, <c>system</c> and <c>systems</c>).
    /// Looser than that would join different companies; the callers still treat a key that fits more than one security as ambiguous.
    /// </summary>
    public static bool Similar(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0)
        {
            return false;
        }

        var a = first.Split(' ');
        var b = second.Split(' ');
        if (a.Length != b.Length || a.Length < 2)
        {
            return false;
        }

        var loose = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                continue;
            }

            var (shorter, longer) = a[i].Length <= b[i].Length ? (a[i], b[i]) : (b[i], a[i]);
            if (shorter.Length < 3 || !longer.StartsWith(shorter, StringComparison.Ordinal) || ++loose > 1)
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"^(?<name>.*?\S)\s*\((?<symbol>[A-Za-z0-9&.\-]{1,30})\)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Bracket();
}
