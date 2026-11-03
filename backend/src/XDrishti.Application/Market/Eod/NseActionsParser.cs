using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using XDrishti.Application.Market.Import;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Eod;

/// <summary>The corporate-actions file turned into the import CSV text (<c>symbol,exDate,kind,numerator,denominator,note</c>) plus counts of what was left out.</summary>
internal sealed record NseActionsText(string Csv, int Splits, int Bonuses, int Other, int Unreadable);

/// <summary>
/// Reads NSE's corporate-actions file (header names in <see cref="EodFormats.ActionsHeader"/>, UNVERIFIED until the probe has run) and turns split and bonus lines into the text the existing import preview reads. Anything else (dividends, rights, AGMs) is counted
/// and left out; a split or bonus whose purpose text cannot be read is counted as unreadable and left out, never guessed. A split "from face value A to face value B" is the ratio B:A reduced; a bonus "a:b" is a:b. An unknown header refuses the file.
/// </summary>
internal static partial class NseActionsParser
{
    private static readonly string[] DateFormats = ["dd-MMM-yyyy", "d-MMM-yyyy", "yyyy-MM-dd"];

    public static async ValueTask<Result<NseActionsText>> ParseAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var input = new StringReader(text.TrimStart('﻿'));
        var reader = new CsvRecordReader(input);
        var headerLine = await NextAsync(reader, cancellationToken);
        var header = headerLine is null ? [] : (await reader.SplitAsync(headerLine, cancellationToken)).Select(h => h.Trim()).ToArray();
        var h = EodFormats.ActionsHeader;
        int[] at = [Index(header, h.Symbol), Index(header, h.Series), Index(header, h.Purpose), Index(header, h.ExDate)];
        if (at.Any(i => i < 0))
        {
            return Result.Failure<NseActionsText>(EodErrors.UnknownFormat("corporate actions", "a column is missing"));
        }

        var csv = new StringBuilder("symbol,exDate,kind,numerator,denominator,note\n");
        int splits = 0, bonuses = 0, other = 0, unreadable = 0;
        while (await NextAsync(reader, cancellationToken) is { } line)
        {
            var f = (await reader.SplitAsync(line, cancellationToken)).Select(c => c.Trim()).ToArray();
            if (f.Length <= at.Max())
            {
                unreadable++;
                continue;
            }

            var purpose = f[at[2]];
            var kind = purpose.Contains("bonus", StringComparison.OrdinalIgnoreCase) ? "bonus"
                : purpose.Contains("split", StringComparison.OrdinalIgnoreCase) || purpose.Contains("sub-division", StringComparison.OrdinalIgnoreCase) || purpose.Contains("sub division", StringComparison.OrdinalIgnoreCase) ? "split"
                : null;
            if (kind is null)
            {
                other++;
                continue;
            }

            var ratio = kind == "bonus" ? Bonus(purpose) : Split(purpose);
            var symbol = f[at[0]];
            if (ratio is null || symbol.Length == 0 || !symbol.All(c => char.IsAsciiLetterOrDigit(c) || c is '&' or '-')
                || !DateOnly.TryParseExact(f[at[3]], DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exDate))
            {
                unreadable++;
                continue;
            }

            csv.Append(CultureInfo.InvariantCulture, $"{symbol},{exDate:yyyy-MM-dd},{kind},{ratio.Value.Numerator},{ratio.Value.Denominator},NSE file\n");
            if (kind == "bonus")
            {
                bonuses++;
            }
            else
            {
                splits++;
            }
        }

        return Result.Success(new NseActionsText(csv.ToString(), splits, bonuses, other, unreadable));
    }

    internal static (int Numerator, int Denominator)? Bonus(string purpose)
    {
        var m = BonusRatio().Match(purpose);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var a) && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var b) && a > 0 && b > 0 ? (a, b) : null;
    }

    internal static (int Numerator, int Denominator)? Split(string purpose)
    {
        var m = FaceValues().Match(purpose);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var from) || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var to) || from <= 0 || to <= 0 || from == to)
        {
            return null;
        }

        var g = Gcd(from, to);
        return (to / g, from / g);
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    private static int Index(string[] header, string name) => Array.FindIndex(header, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

    private static async ValueTask<string?> NextAsync(CsvRecordReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    [GeneratedRegex(@"(\d+)\s*:\s*(\d+)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex BonusRatio();

    [GeneratedRegex(@"from\s+r[se]?\.?\s*(\d+)(?:\.0+)?(?!\.?\d).*?to\s+r[se]?\.?\s*(\d+)(?:\.0+)?(?!\.?\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 200)]
    private static partial Regex FaceValues();
}
