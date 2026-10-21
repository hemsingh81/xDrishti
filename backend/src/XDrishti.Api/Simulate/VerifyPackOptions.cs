using System.Globalization;
using XDrishti.Application.Research;
using XDrishti.SimExport.Verification;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The command line of <c>verify-pack</c> (spec P3-09, V4), parsed strictly and purely by the rules of <see cref="SimulateOptions"/> (the same tokenizer, the same readers of symbols, basket, dates and the cost choice, the invariant culture). The
/// seed is required and has no default: a default would be a hidden random source, and a seed chosen after the trades were seen is not a pre-registration. There is no <c>--param</c>: a pack checks the rule set as it ships. The mode
/// <c>--check &lt;log&gt;</c> is used alone; the modes <c>--assist &lt;pack-folder&gt;</c> (spec P3-09c) and <c>--gate &lt;pack-folder&gt;</c> (spec P3-09d) take only <c>--second-source-folder</c>. The arguments are those after the verb name.
/// </summary>
internal sealed record VerifyPackOptions(
    string? CheckPath,
    IReadOnlyList<string> Strategies,
    IReadOnlyList<string> Symbols,
    string? Basket,
    DateOnly From,
    DateOnly To,
    string Seed,
    int Count,
    CostChoice Costs,
    bool AcknowledgeCsvAdjusted,
    bool Force,
    string? AssistPath = null,
    string? SecondSourceFolder = null,
    string? GatePath = null)
{
    /// <summary>The usage text (stderr on a usage error).</summary>
    public const string Usage =
        "usage:\n"
        + "  verify-pack --strategies <id[,id...]> (--symbols <a,b,...> | --basket <name|id>) --from <yyyy-MM-dd> --to <yyyy-MM-dd> --seed <text>\n"
        + "              [--count <1..100>]   (random trades; default 20)\n"
        + "              (--gross | --costs <sheet-id> [--capital <rupees>] [--profile standard|conservative] [--max-cost-r <0..100>])   (the cost choice is required)\n"
        + "              [--acknowledge-csv-adjusted] [--force]\n"
        + "  verify-pack --check <path-to-check-log.csv>\n"
        + "  verify-pack --assist <pack-folder> [--second-source-folder <dir>]   (an automatic second read of a pack; not a signed check)\n"
        + "  verify-pack --gate <pack-folder> [--second-source-folder <dir>]   (the automated gate: the assisted read, then the rules; writes gate-decision.json and .md; exit 0 PASS or PASS_WITH_DATA_FINDINGS, 10 RERUN_NEEDED, 11 DATA_REPAIR_NEEDED, 12 BLOCKED_DEFECT)\n"
        + "The seed is a text of letters, digits, '.', '_' and '-' (at most 40) that you write down before the first draw; it has no default.\n"
        + "Quote a symbol list in the shell: --symbols 'M&M,RELIANCE'\n";

    private static readonly string[] ValueFlags =
        ["--strategies", "--symbols", "--basket", "--from", "--to", "--seed", "--count", "--check", "--costs", "--capital", "--profile", "--max-cost-r", "--assist", "--gate", "--second-source-folder"];

    private static readonly string[] SwitchFlags = ["--gross", "--acknowledge-csv-adjusted", "--force"];

    private const int MaxStrategyLength = 40;

    /// <summary>Parses the arguments after the verb name.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="knownStrategyIds">The ids of the built-in strategies: an unknown id, or one that differs from a known one only in case, is a usage error naming the known ids.</param>
    public static VerifyPackParse Parse(IReadOnlyList<string> args, IReadOnlyCollection<string> knownStrategyIds)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(knownStrategyIds);

        var errors = new List<string>();
        var (values, switches, parameters) = SimulateOptions.Tokenize(args, ValueFlags, SwitchFlags, errors);
        if (values.TryGetValue("--check", out var path))
        {
            if (errors.Count == 0 && (values.Count > 1 || switches.Count > 0))
            {
                errors.Add("'--check' is used alone");
            }

            return errors.Count > 0
                ? Invalid(errors)
                : new VerifyPackParse(new VerifyPackOptions(path, [], [], null, default, default, string.Empty, 0, CostChoice.Gross, false, false), []);
        }

        var gate = values.TryGetValue("--gate", out var gateFolder);
        if (gate || values.TryGetValue("--assist", out _))
        {
            var mode = gate ? "--gate" : "--assist";
            var folder = gate ? gateFolder : values["--assist"];
            if (errors.Count == 0 && (values.Keys.Any(k => k != mode && k != "--second-source-folder") || switches.Count > 0))
            {
                errors.Add("'" + mode + "' is used with '--second-source-folder' only (and '--assist' and '--gate' are not used together)");
            }

            return errors.Count > 0
                ? Invalid(errors)
                : new VerifyPackParse(new VerifyPackOptions(null, [], [], null, default, default, string.Empty, 0, CostChoice.Gross, false, false, gate ? null : folder, values.GetValueOrDefault("--second-source-folder"), gate ? folder : null), []);
        }

        if (values.ContainsKey("--second-source-folder"))
        {
            errors.Add("'--second-source-folder' is used with '--assist' or '--gate'");
        }

        var strategies = ReadStrategies(values, knownStrategyIds, errors);
        var symbols = SimulateOptions.ReadSymbols(values, errors);
        var basket = SimulateOptions.ReadBasket(values, errors);
        if (values.ContainsKey("--symbols") == values.ContainsKey("--basket"))
        {
            errors.Add(values.ContainsKey("--symbols") ? "give --symbols or --basket, not both" : "give --symbols or --basket");
        }

        var from = SimulateOptions.Date(values, "--from", errors);
        var to = SimulateOptions.Date(values, "--to", errors);
        var seed = ReadSeed(values, errors);
        var count = ReadCount(values, errors);
        var costs = SimulateOptions.ReadCosts(values, switches, errors);
        if (parameters.Count > 0)
        {
            errors.Add("unknown option '--param'");
        }

        return errors.Count > 0
            ? Invalid(errors)
            : new VerifyPackParse(new VerifyPackOptions(null, strategies, symbols, basket, from!.Value, to!.Value, seed!, count, costs, switches.Contains("--acknowledge-csv-adjusted"), switches.Contains("--force")), []);
    }

    private static VerifyPackParse Invalid(List<string> errors) => new(null, errors.AsReadOnly());

    private static List<string> ReadStrategies(Dictionary<string, string> values, IReadOnlyCollection<string> known, List<string> errors)
    {
        var ids = new List<string>();
        if (!values.TryGetValue("--strategies", out var list))
        {
            errors.Add("'--strategies' is required");
            return ids;
        }

        var listed = string.Join(", ", known.Order(StringComparer.Ordinal));
        foreach (var id in list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (id.Length > MaxStrategyLength || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                errors.Add("'--strategies' has an entry that is not an id of letters, digits and underscores: '" + SimulateOptions.Show(id) + "'");
            }
            else if (!known.Contains(id, StringComparer.Ordinal))
            {
                var exact = known.FirstOrDefault(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));
                errors.Add(exact is null
                    ? "'--strategies' names '" + id + "', which is not a built-in strategy (ids: " + listed + ")"
                    : "'--strategies' is case-sensitive: use '" + exact + "', not '" + id + "' (ids: " + listed + ")");
            }
            else if (ids.Contains(id, StringComparer.Ordinal))
            {
                errors.Add("'--strategies' names '" + id + "' twice");
            }
            else
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0 && errors.Count == 0)
        {
            errors.Add("'--strategies' names no strategy");
        }

        return ids;
    }

    private static string? ReadSeed(Dictionary<string, string> values, List<string> errors)
    {
        if (!values.TryGetValue("--seed", out var seed))
        {
            errors.Add("'--seed' is required: write a seed down before the first draw (letters, digits, '.', '_' and '-', at most 40); it has no default");
            return null;
        }

        if (!VerificationSampler.IsValidSeed(seed))
        {
            errors.Add("'--seed' must be 1 to " + VerificationSampler.MaxSeedLength.ToString(CultureInfo.InvariantCulture) + " characters of letters, digits, '.', '_' and '-', '" + SimulateOptions.Show(seed) + "' is not");
            return null;
        }

        return seed;
    }

    private static int ReadCount(Dictionary<string, string> values, List<string> errors)
    {
        if (!values.TryGetValue("--count", out var text))
        {
            return VerificationSampler.DefaultCount;
        }

        if (text.Length is >= 1 and <= 3 && text.All(char.IsAsciiDigit) && int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) is >= 1 and <= VerificationSampler.MaxCount && (text.Length == 1 || text[0] != '0'))
        {
            return int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        errors.Add("'--count' must be a whole number from 1 to " + VerificationSampler.MaxCount.ToString(CultureInfo.InvariantCulture) + ", '" + SimulateOptions.Show(text) + "' is not");
        return VerificationSampler.DefaultCount;
    }
}
