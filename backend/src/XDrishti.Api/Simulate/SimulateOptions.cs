using System.Globalization;
using XDrishti.Application.Research;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The command line of <c>simulate</c> (spec P3-04b2, D1), parsed strictly and purely: <c>--flag value</c> and <c>--flag=value</c>; a flag at most once except <c>--param</c> (whose names are also unique); an unknown flag, a missing value, a
/// repeat, none or two universe flags, or a missing <c>--strategy</c>, <c>--from</c> or <c>--to</c> is a usage error. Everything is read in the invariant culture, so the result does not depend on the machine's language. The arguments are those after
/// the verb name. The parser knows no strategy; it is given the known ids only to tell the owner that an id differs in case.
/// </summary>
internal sealed record SimulateOptions(
    bool ListStrategies,
    string StrategyId,
    IReadOnlyList<string> Symbols,
    string? Basket,
    DateOnly From,
    DateOnly To,
    string? Timeframe,
    IReadOnlyDictionary<string, double> Parameters,
    FillsMode Fills,
    bool AcknowledgeCsvAdjusted,
    bool Force,
    CostChoice Costs,
    int Cards = SimulateOptions.DefaultCards,
    bool Store = false)
{
    /// <summary>The default of <c>--cards</c>: the roadmap's cap (spec P3-04c, C1).</summary>
    public const int DefaultCards = 200;

    /// <summary>The usage text (stderr on a usage error).</summary>
    public const string Usage =
        "usage:\n"
        + "  simulate --strategy <id> (--symbols <a,b,...> | --basket <name|id>) --from <yyyy-MM-dd> --to <yyyy-MM-dd>\n"
        + "           [--timeframe <5m|10m|15m|30m|1h|1D>] [--param <name>=<number>]... [--fills modelled|no-slippage] [--acknowledge-csv-adjusted] [--force]\n"
        + "           [--cards <0..500>]   (trade cards and index.html; default 200, 0 writes none)\n"
        + "           [--store]   (keep the run in the database as well as in the export folder; default off, P3-10)\n"
        + "           (--gross | --costs <sheet-id> [--capital <rupees>] [--profile standard|conservative] [--max-cost-r <0..100>])   (the cost choice is required, P3-03f)\n"
        + "  simulate --list-strategies\n"
        + "Quote a symbol list in the shell: --symbols 'M&M,RELIANCE'\n";

    private const int MaxEntryLength = 40;

    private const int MaxText = 60;

    private static readonly string[] ValueFlags = ["--strategy", "--symbols", "--basket", "--from", "--to", "--timeframe", "--param", "--fills", "--cards", "--costs", "--capital", "--profile", "--max-cost-r"];

    private static readonly string[] SwitchFlags = ["--acknowledge-csv-adjusted", "--force", "--list-strategies", "--gross", "--store"];

    /// <summary>Parses the arguments after the verb name.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="knownStrategyIds">The ids of the built-in strategies, when the caller has them: an id that differs from one only in case is a usage error (ids are case-sensitive). Null: not checked.</param>
    public static SimulateParse Parse(IReadOnlyList<string> args, IReadOnlyCollection<string>? knownStrategyIds = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var switches = new HashSet<string>(StringComparer.Ordinal);
        var parameters = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add("unexpected argument '" + Show(token) + "'");
                continue;
            }

            var equals = token.IndexOf('=', StringComparison.Ordinal);
            var flag = equals < 0 ? token : token[..equals];
            var inline = equals < 0 ? null : token[(equals + 1)..];
            if (SwitchFlags.Contains(flag, StringComparer.Ordinal))
            {
                if (inline is not null)
                {
                    errors.Add("'" + flag + "' takes no value");
                }
                else if (!switches.Add(flag))
                {
                    errors.Add("'" + flag + "' is given more than once");
                }

                continue;
            }

            var known = ValueFlags.Contains(flag, StringComparer.Ordinal);
            string? value = inline;
            if (value is null && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            if (!known)
            {
                errors.Add("unknown option '" + Show(flag) + "'");
            }
            else if (string.IsNullOrEmpty(value))
            {
                errors.Add("'" + flag + "' needs a value");
            }
            else if (flag == "--param")
            {
                parameters.Add(value);
            }
            else if (!values.TryAdd(flag, value))
            {
                errors.Add("'" + flag + "' is given more than once");
            }
        }

        return Build(values, switches, parameters, knownStrategyIds, errors);
    }

    private static SimulateParse Build(
        Dictionary<string, string> values, HashSet<string> switches, List<string> parameters, IReadOnlyCollection<string>? knownStrategyIds, List<string> errors)
    {
        if (switches.Contains("--list-strategies"))
        {
            if (errors.Count == 0 && (values.Count > 0 || parameters.Count > 0 || switches.Count > 1))
            {
                errors.Add("'--list-strategies' is used alone");
            }

            return errors.Count > 0
                ? Invalid(errors)
                : new SimulateParse(
                    new SimulateOptions(true, string.Empty, [], null, default, default, null, new Dictionary<string, double>(), FillsMode.Modelled, false, false, CostChoice.Gross), []);
        }

        var strategy = Strategy(values, knownStrategyIds, errors);
        var symbols = ReadSymbols(values, errors);
        var basket = ReadBasket(values, errors);
        if (values.ContainsKey("--symbols") == values.ContainsKey("--basket"))
        {
            errors.Add(values.ContainsKey("--symbols") ? "give --symbols or --basket, not both" : "give --symbols or --basket");
        }

        var from = Date(values, "--from", errors);
        var to = Date(values, "--to", errors);
        var timeframe = ReadTimeframe(values, errors);
        var fills = ReadFills(values, errors);
        var cards = ReadCards(values, errors);
        var costs = ReadCosts(values, switches, errors);
        var named = ReadParameters(parameters, errors);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        return new SimulateParse(
            new SimulateOptions(false, strategy!, symbols, basket, from!.Value, to!.Value, timeframe, named, fills, switches.Contains("--acknowledge-csv-adjusted"), switches.Contains("--force"), costs, cards, switches.Contains("--store")), []);
    }

    private static SimulateParse Invalid(List<string> errors) => new(null, errors.AsReadOnly());

    private static string? Strategy(Dictionary<string, string> values, IReadOnlyCollection<string>? known, List<string> errors)
    {
        if (!values.TryGetValue("--strategy", out var id))
        {
            errors.Add("'--strategy' is required");
            return null;
        }

        if (id.Length > MaxEntryLength || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            errors.Add("'--strategy' must be an id of letters, digits and underscores, '" + Show(id) + "' is not");
            return null;
        }

        if (known is not null && !known.Contains(id, StringComparer.Ordinal) && known.FirstOrDefault(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            errors.Add("'--strategy' is case-sensitive: use '" + exact + "', not '" + id + "' (ids: " + string.Join(", ", known.Order(StringComparer.Ordinal)) + ")");
            return null;
        }

        return id;
    }

    private static List<string> ReadSymbols(Dictionary<string, string> values, List<string> errors)
    {
        var symbols = new List<string>();
        if (!values.TryGetValue("--symbols", out var list))
        {
            return symbols;
        }

        foreach (var entry in list.Split([',', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.Length > MaxEntryLength || !entry.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '&' or '.' or '_' or '-'))
            {
                errors.Add("'--symbols' has an entry that is not a symbol (letters, digits and : & . _ - only): '" + Show(entry) + "'");
            }
            else
            {
                symbols.Add(entry);
            }
        }

        if (symbols.Count == 0 && errors.Count == 0)
        {
            errors.Add("'--symbols' names no symbol");
        }

        return symbols;
    }

    private static string? ReadBasket(Dictionary<string, string> values, List<string> errors)
    {
        if (!values.TryGetValue("--basket", out var basket))
        {
            return null;
        }

        if (basket.Length > 100 || basket.Any(char.IsControl) || string.IsNullOrWhiteSpace(basket))
        {
            errors.Add("'--basket' must be a name or an id without control characters");
            return null;
        }

        return basket.Trim();
    }

    private static DateOnly? Date(Dictionary<string, string> values, string flag, List<string> errors)
    {
        if (!values.TryGetValue(flag, out var text))
        {
            errors.Add("'" + flag + "' is required");
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add("'" + flag + "' must be a date as yyyy-MM-dd, '" + Show(text) + "' is not");
        return null;
    }

    private static string? ReadTimeframe(Dictionary<string, string> values, List<string> errors)
    {
        if (!values.TryGetValue("--timeframe", out var code))
        {
            return null;
        }

        if (Timeframes.TryParse(code, out var timeframe) && Timeframes.Aggregated.Contains(timeframe))
        {
            return timeframe.ToCode();
        }

        errors.Add("'--timeframe' must be a derived timeframe (5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M), '" + Show(code) + "' is not");
        return null;
    }

    private static FillsMode ReadFills(Dictionary<string, string> values, List<string> errors)
    {
        if (!values.TryGetValue("--fills", out var mode))
        {
            return FillsMode.Modelled;
        }

        switch (mode)
        {
            case "modelled": return FillsMode.Modelled;
            case "no-slippage": return FillsMode.NoSlippage;
            default:
                errors.Add("'--fills' must be modelled or no-slippage, '" + Show(mode) + "' is not");
                return FillsMode.Modelled;
        }
    }

    private static int ReadCards(Dictionary<string, string> values, List<string> errors)
    {
        if (!values.TryGetValue("--cards", out var text))
        {
            return DefaultCards;
        }

        if (text.Length is >= 1 and <= 3 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0') && int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) <= CardSelection.MaxCap)
        {
            return int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        errors.Add("'--cards' must be a whole number from 0 to " + CardSelection.MaxCap.ToString(CultureInfo.InvariantCulture) + ", '" + Show(text) + "' is not");
        return DefaultCards;
    }

    /// <summary>
    /// The cost choice (spec P3-03f, F1): exactly one of <c>--gross</c> and <c>--costs &lt;id&gt;</c>; <c>--capital</c>, <c>--profile</c> and <c>--max-cost-r</c> need <c>--costs</c>. The same bounds as the request validation, so that a typo is a usage
    /// error (exit 2) and not a refusal.
    /// </summary>
    private static CostChoice ReadCosts(Dictionary<string, string> values, HashSet<string> switches, List<string> errors)
    {
        var gross = switches.Contains("--gross");
        var named = values.TryGetValue("--costs", out var id);
        if (gross == named)
        {
            errors.Add(gross ? "give --gross or --costs, not both" : "give --gross (no charges, labelled 'costs: none') or --costs <sheet-id>: a run is never costed or gross by omission");
            return CostChoice.Gross;
        }

        string[] sheetOnly = ["--capital", "--profile", "--max-cost-r"];
        if (gross)
        {
            foreach (var flag in sheetOnly.Where(values.ContainsKey))
            {
                errors.Add("'" + flag + "' needs --costs <sheet-id>");
            }

            return CostChoice.Gross;
        }

        if (!CostSheet.IsValidId(id!))
        {
            errors.Add("'--costs' must be a sheet id of lower case letters, digits, '.', '_' or '-' (at most 40), '" + Show(id!) + "' is not");
        }

        var capital = CostChoice.DefaultCapital;
        if (values.TryGetValue("--capital", out var capitalText))
        {
            if (!decimal.TryParse(capitalText, NumberStyles.Number, CultureInfo.InvariantCulture, out capital) || !(capital > 0 && capital <= CostContext.MaxCapital))
            {
                errors.Add("'--capital' must be a number of rupees above 0 and at most " + CostContext.MaxCapital.ToString("0", CultureInfo.InvariantCulture) + ", '" + Show(capitalText) + "' is not");
            }
        }

        var profile = CostChoice.DefaultProfile;
        if (values.TryGetValue("--profile", out var profileText))
        {
            profile = profileText;
            if (profile is not ("standard" or "conservative"))
            {
                errors.Add("'--profile' must be standard or conservative, '" + Show(profileText) + "' is not");
            }
        }

        var maxCostR = CostChoice.DefaultMaxCostR;
        if (values.TryGetValue("--max-cost-r", out var maxText)
            && (!decimal.TryParse(maxText, NumberStyles.Number, CultureInfo.InvariantCulture, out maxCostR) || !(maxCostR > 0 && maxCostR <= CostContext.MaxCostRLimit)))
        {
            errors.Add("'--max-cost-r' must be a number above 0 and at most " + CostContext.MaxCostRLimit.ToString("0", CultureInfo.InvariantCulture) + ", '" + Show(maxText) + "' is not");
        }

        return CostChoice.Sheet(id!, capital, profile, maxCostR);
    }

    private static Dictionary<string, double> ReadParameters(List<string> parameters, List<string> errors)
    {
        var named = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            var equals = parameter.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? parameter : parameter[..equals];
            var text = equals < 0 ? string.Empty : parameter[(equals + 1)..];
            if (!IsParameterName(name))
            {
                errors.Add("'--param' needs a name of lower-case letters, digits and underscores before '=', '" + Show(parameter) + "' has not");
            }
            else if (!TryNumber(text, out var number))
            {
                errors.Add("'--param " + name + "' must be a finite number such as 15 or 2.5, '" + Show(text) + "' is not");
            }
            else if (!named.TryAdd(name, number))
            {
                errors.Add("'--param " + name + "' is given more than once");
            }
        }

        return named;
    }

    private static bool IsParameterName(string name) =>
        name.Length is >= 1 and <= 32 && name[0] is >= 'a' and <= 'z' && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static bool TryNumber(string text, out double number)
    {
        number = 0;
        return text.Length > 0
            && !text.Any(char.IsWhiteSpace)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number);
    }

    /// <summary>A value as it is echoed to the console: cut, and with control characters replaced, so that an argument cannot write escape sequences to the terminal.</summary>
    internal static string Show(string text)
    {
        var cut = text.Length > MaxText ? text[..MaxText] + "..." : text;
        return string.Concat(cut.Select(c => char.IsControl(c) ? '?' : c));
    }
}
