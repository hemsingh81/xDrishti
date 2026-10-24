using System.Globalization;
using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// The one query model of the report endpoints (P8-00 D4): the run set, the basis, the lens, the filters, the figure of a pivot and the page of a trade list, every name checked against a whitelist. A name the whitelist does not know is
/// <c>reports.invalid</c> (422), never a silent empty answer. There is no way here to say that the owner confirmed the charge sheet: the basis <see cref="ReportBasis.Net"/> is only a request, allowed or refused by the stored confirmation.
/// </summary>
/// <param name="Runs">The run keys asked for; empty means the default set.</param>
/// <param name="Basis">Gross or net R.</param>
/// <param name="Lens">The dimensions to group by, in order.</param>
/// <param name="Filters">The filters.</param>
/// <param name="Metric">The figure of a pivot.</param>
/// <param name="Page">The page of a trade list.</param>
public sealed partial record ReportQuery(
    IReadOnlyList<string> Runs,
    ReportBasis Basis,
    IReadOnlyList<ReportDimension> Lens,
    ReportFilters Filters,
    ReportMetric Metric,
    PageRequest Page)
{
    /// <summary>The page size of a trade list when none is asked for.</summary>
    public const int DefaultLimit = 100;

    private const int MaxListItems = 50;

    /// <summary>The query with every default: the default run set, gross, the whole population, no filter.</summary>
    public static ReportQuery Default { get; } = new([], ReportBasis.Gross, [], ReportFilters.None, ReportMetric.ExpectancyR, new PageRequest(DefaultLimit));

    /// <summary>Checks every name and value of the raw input against its whitelist. All problems are reported together in one <c>reports.invalid</c>.</summary>
    /// <param name="input">The raw query string values.</param>
    public static Result<ReportQuery> Parse(ReportQueryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new List<string>();
        var runs = Values(input.Runs, "runs", problems, run => RunRecordText.IsHex64(run) ? run : null, "a run key is 64 lower-case hex characters");
        var basis = ParseBasis(input.Basis, problems);
        var lens = ParseLens(input.Lens, problems);
        var strategies = Values(input.Strategy, "strategy", problems, v => StrategyId().IsMatch(v) ? v : null, "a strategy id starts with a letter and holds letters, digits and underscores");
        var timeframes = Values(input.Timeframe, "timeframe", problems, v => Timeframes.TryParse(v, out _) ? v : null, "a timeframe is 1m 5m 10m 15m 30m 1h 1D 1W or 1M");
        var sides = Values(input.Side, "side", problems, v => v.ToUpperInvariant() is "BUY" or "SELL" ? v.ToUpperInvariant() : null, "a side is BUY or SELL");
        var styles = Values(input.Style, "style", problems, v => v is "intraday" or "swing" ? v : null, "a style is intraday or swing");
        var exits = Values(input.Exit, "exit", problems, v => ExitLabel().IsMatch(v) ? v : null, "an exit label holds letters, digits and . _ { } = , | -; separate several with a semicolon", ';');
        var baskets = Values(input.Basket, "basket", problems, v => Guid.TryParseExact(v, "D", out var id) ? id.ToString("D") : null, "a basket is a guid");
        var symbols = Values(input.Symbol, "symbol", problems, v => Symbol().IsMatch(v) ? v : null, "a symbol holds letters, digits and : . _ & -");
        var from = ParseDate(input.From, "from", problems);
        var to = ParseDate(input.To, "to", problems);
        if (from is { } f && to is { } t && f > t)
        {
            problems.Add("from is after to");
        }

        var metric = ReportNames.TryParseMetric(input.Metric, out var parsedMetric);
        if (!metric)
        {
            problems.Add($"metric '{Trim(input.Metric)}' is not one of {string.Join(", ", ReportNames.MetricNames)}");
        }

        var limit = DefaultLimit;
        if (!string.IsNullOrEmpty(input.Limit) && (!int.TryParse(input.Limit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > PageRequest.MaxSize))
        {
            problems.Add($"limit is a whole number from 1 to {PageRequest.MaxSize}");
        }

        if (input.Cursor is not null && !Cursor().IsMatch(input.Cursor))
        {
            problems.Add("the cursor is not one this endpoint returned");
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ReportQuery>(ReportErrors.Invalid(string.Join("; ", problems)));
        }

        var filters = new ReportFilters(strategies, timeframes, sides, styles, exits, [.. baskets.Select(Guid.Parse)], symbols, from, to);
        return Result.Success(new ReportQuery(runs, basis, lens, filters, parsedMetric, new PageRequest(limit, input.Cursor)));
    }

    /// <summary>The offset a trade-list cursor stands for (0 for the first page).</summary>
    public int Offset => Page.Cursor is null ? 0 : int.Parse(Page.Cursor[1..], CultureInfo.InvariantCulture);

    /// <summary>The cursor of the page that starts at <paramref name="offset"/>.</summary>
    /// <param name="offset">How many trades the earlier pages held.</param>
    public static string CursorFor(int offset) => "o" + offset.ToString(CultureInfo.InvariantCulture);

    private static ReportBasis ParseBasis(string? text, List<string> problems)
    {
        switch (text)
        {
            case null or "" or "gross":
                return ReportBasis.Gross;
            case "net":
                return ReportBasis.Net;
            default:
                problems.Add($"basis '{Trim(text)}' is gross or net");
                return ReportBasis.Gross;
        }
    }

    private static List<ReportDimension> ParseLens(string? text, List<string> problems)
    {
        var lens = new List<ReportDimension>();
        foreach (var name in Split(text, ','))
        {
            if (!ReportNames.TryParseDimension(name, out var dimension))
            {
                problems.Add($"lens '{Trim(name)}' is not one of {string.Join(", ", ReportNames.DimensionNames)}");
            }
            else if (lens.Contains(dimension))
            {
                problems.Add($"lens '{name}' is repeated");
            }
            else
            {
                lens.Add(dimension);
            }
        }

        if (lens.Count > 3)
        {
            problems.Add("a lens has at most 3 dimensions");
        }

        return lens;
    }

    private static List<T> Values<T>(string? text, string field, List<string> problems, Func<string, T?> check, string rule, char separator = ',')
        where T : class
    {
        var items = new List<T>();
        var parts = Split(text, separator);
        if (parts.Count > MaxListItems)
        {
            problems.Add($"{field} has more than {MaxListItems} values");
            return items;
        }

        foreach (var part in parts)
        {
            var value = part.Length <= 80 ? check(part) : null;
            if (value is null)
            {
                problems.Add($"{field} '{Trim(part)}' is not valid: {rule}");
            }
            else if (!items.Contains(value))
            {
                items.Add(value);
            }
        }

        return items;
    }

    private static DateOnly? ParseDate(string? text, string field, List<string> problems)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        problems.Add($"{field} is a date written yyyy-MM-dd");
        return null;
    }

    private static List<string> Split(string? text, char separator) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>A rejected value is echoed back cut short and without control characters, so a problem never carries a long or odd string.</summary>
    private static string Trim(string? value)
    {
        var text = value ?? string.Empty;
        var clean = new string([.. text.Where(c => !char.IsControl(c))]);
        return clean.Length > 24 ? clean[..24] + "..." : clean;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex StrategyId();

    [GeneratedRegex(@"^[A-Za-z0-9_.{}=,|\-]{1,80}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ExitLabel();

    [GeneratedRegex(@"^[A-Za-z0-9:._&\-]{1,40}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Symbol();

    [GeneratedRegex("^o[0-9]{1,9}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Cursor();
}
