using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>What the <c>xd</c> verb was asked to do (spec P5-07).</summary>
internal enum XdAction
{
    StrategyValidate,
    StrategyTest,
    LabRuns,
}

/// <summary>The outcome of parsing the arguments after <c>xd</c>: the options, or the usage errors (never both, never neither).</summary>
internal sealed record XdParse(XdOptions? Options, IReadOnlyList<string> Errors);

/// <summary>The parsed command line of the <c>xd</c> verb. Nothing here reads a file or the database.</summary>
internal sealed record XdOptions(
    XdAction Action,
    string? File,
    IReadOnlyList<string> Symbols,
    Guid? BasketId,
    string Method,
    DateOnly? From,
    DateOnly? To,
    string? Note,
    int TimeoutMinutes,
    string? Strategy,
    string? State,
    int? Limit)
{
    public const int DefaultTimeoutMinutes = 60;

    public const int MaxTimeoutMinutes = 24 * 60;

    public const string Usage = """
        usage:
          xd strategy validate FILE.yaml
          xd strategy test FILE.yaml (--symbols 'A,B' | --basket-id GUID) [--method quick|full] [--from YYYY-MM-DD --to YYYY-MM-DD] [--note TEXT] [--timeout-minutes N]
          xd learn cells --as-of YYYY-MM-DD --run RUNKEY:STYLE:EXIT [--run ...] [--half-life-days N] [--prior-strength N] [--min-trades N]
          xd learn features --run RUNKEY [--run ...]
          xd learn phase-check [--replay-seconds X]
          xd learn training-set --run RUNKEY:STYLE:EXIT [--run ...] [--require NAME,NAME,...] [--half-life-days N] [--prior-strength N] [--min-trades N]
          xd lab runs [--strategy ID] [--state queued|running|succeeded|failed|cancelled|interrupted] [--limit 1..200]
        exit codes: 0 done (a full test: verdict PASS), 1 refused (located problems on stderr), 2 wrong command line, 3 failure or timeout, 4 full test finished with verdict MARGINAL or FAIL, 130 cancelled

        """;

    private static readonly string[] Known = ["--symbols", "--basket-id", "--method", "--from", "--to", "--note", "--timeout-minutes", "--strategy", "--state", "--limit"];

    private static readonly string[] TestOptions = ["--symbols", "--basket-id", "--method", "--from", "--to", "--note", "--timeout-minutes"];

    private static readonly string[] RunsOptions = ["--strategy", "--state", "--limit"];

    /// <summary>Parses the arguments after the verb name.</summary>
    public static XdParse Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        if (args.Count < 2)
        {
            return Fail(errors, "a command is required");
        }

        XdAction? action = (args[0], args[1]) switch
        {
            ("strategy", "validate") => XdAction.StrategyValidate,
            ("strategy", "test") => XdAction.StrategyTest,
            ("lab", "runs") => XdAction.LabRuns,
            _ => null,
        };
        if (action is null)
        {
            return Fail(errors, $"unknown command '{SimulateOptions.Show(args[0])} {SimulateOptions.Show(args[1])}'");
        }

        var positional = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 2; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
            }
            else if (!Known.Contains(arg, StringComparer.Ordinal))
            {
                errors.Add($"unknown option '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if (!values.TryAdd(arg, args[++i]))
            {
                errors.Add($"'{arg}' is given twice");
            }
        }

        var needsFile = action != XdAction.LabRuns;
        if (positional.Count != (needsFile ? 1 : 0))
        {
            errors.Add(needsFile ? "exactly one strategy file is required" : "'lab runs' takes no file");
        }

        var allowed = action switch
        {
            XdAction.StrategyValidate => [],
            XdAction.StrategyTest => TestOptions,
            _ => RunsOptions,
        };
        errors.AddRange(values.Keys.Where(k => !allowed.Contains(k, StringComparer.Ordinal)).Select(k => $"'{k}' does not apply to this command"));

        var symbols = values.TryGetValue("--symbols", out var list)
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        Guid? basket = null;
        if (values.TryGetValue("--basket-id", out var basketText))
        {
            if (Guid.TryParse(basketText, out var id))
            {
                basket = id;
            }
            else
            {
                errors.Add("--basket-id must be a GUID");
            }
        }

        if (action == XdAction.StrategyTest && (symbols.Length > 0) == basket.HasValue)
        {
            errors.Add("give exactly one of --symbols and --basket-id");
        }

        var method = values.GetValueOrDefault("--method", "quick");
        if (method is not ("quick" or "full"))
        {
            errors.Add("--method must be quick or full");
        }

        var from = Date(values, "--from", errors);
        var to = Date(values, "--to", errors);
        if (values.ContainsKey("--from") != values.ContainsKey("--to"))
        {
            errors.Add("give both --from and --to, or neither");
        }

        var timeout = Number(values, "--timeout-minutes", DefaultTimeoutMinutes, 1, MaxTimeoutMinutes, errors) ?? DefaultTimeoutMinutes;
        var limit = Number(values, "--limit", null, 1, 200, errors);
        var note = values.GetValueOrDefault("--note");
        if (note is { Length: > 500 })
        {
            errors.Add("--note is at most 500 characters");
        }

        return errors.Count > 0
            ? new XdParse(null, errors)
            : new XdParse(new XdOptions(action.Value, positional.FirstOrDefault(), symbols, basket, method, from, to, note, timeout, values.GetValueOrDefault("--strategy"), values.GetValueOrDefault("--state"), limit), []);
    }

    private static DateOnly? Date(Dictionary<string, string> values, string key, List<string> errors)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add($"{key} must be a date as YYYY-MM-DD");
        return null;
    }

    private static int? Number(Dictionary<string, string> values, string key, int? fallback, int min, int max, List<string> errors)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max)
        {
            return n;
        }

        errors.Add($"{key} must be a whole number from {min} to {max}");
        return fallback;
    }

    private static XdParse Fail(List<string> errors, string message)
    {
        errors.Add(message);
        return new XdParse(null, errors);
    }
}
