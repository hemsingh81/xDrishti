using System.Globalization;
using XDrishti.Domain.Market.Bars;

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
    bool Force)
{
    /// <summary>The usage text (stderr on a usage error).</summary>
    public const string Usage =
        "usage:\n"
        + "  simulate --strategy <id> (--symbols <a,b,...> | --basket <name|id>) --from <yyyy-MM-dd> --to <yyyy-MM-dd>\n"
        + "           [--timeframe <5m|15m>] [--param <name>=<number>]... [--fills modelled|no-slippage] [--acknowledge-csv-adjusted] [--force]\n"
        + "  simulate --list-strategies\n"
        + "Quote a symbol list in the shell: --symbols 'M&M,RELIANCE'\n";

    private const int MaxEntryLength = 40;

    private const int MaxText = 60;

    private static readonly string[] ValueFlags = ["--strategy", "--symbols", "--basket", "--from", "--to", "--timeframe", "--param", "--fills"];

    private static readonly string[] SwitchFlags = ["--acknowledge-csv-adjusted", "--force", "--list-strategies"];

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
                    new SimulateOptions(true, string.Empty, [], null, default, default, null, new Dictionary<string, double>(), FillsMode.Modelled, false, false), []);
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
        var named = ReadParameters(parameters, errors);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        return new SimulateParse(
            new SimulateOptions(false, strategy!, symbols, basket, from!.Value, to!.Value, timeframe, named, fills, switches.Contains("--acknowledge-csv-adjusted"), switches.Contains("--force")), []);
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

        if (Timeframes.TryParse(code, out var timeframe) && Timeframes.Intraday.Contains(timeframe))
        {
            return timeframe.ToCode();
        }

        errors.Add("'--timeframe' must be an intraday timeframe such as 5m or 15m, '" + Show(code) + "' is not");
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
