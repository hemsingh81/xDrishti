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
          xd filings extract --from YYYY-MM-DD --to YYYY-MM-DD [--limit N]   classify stored announcements with the local model (needs the assistant switch on; no tool, schema-checked)
          xd options spread-model build [--underlying SYMBOL]   build the option half-spread model from usable chain sessions (fewer than the setting: no model, the reason is printed)
          xd options remark --run RUNKEY --profile HASH   re-mark each trade of a stored cash run as a single long option (option_buy profile) or as a debit or credit spread (debit_spread, credit_spread profile) with its legs (every trade gets one row, paired or unpaired with its reason; research only)
          xd options condors --profile HASH --from YYYY-MM-DD --to YYYY-MM-DD [--underlying SYMBOL]   simulate the iron condor of the index range family (needs a stored regime and a measured spread model; research only; the printed population key is the --run of the evaluation)
          xd options expression-eval --family expr:option_buy|expr:debit_spread|expr:credit_spread|expr:iron_condor --run RUNKEY [--profile HASH] [--holdout]   protocol O verdict (KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE with the missing quantities named) for a re-marked full-test cash run; the trial is counted in the ledger first; research only, the expression stays cash
          xd options phase-check [--cut FAMILY[,FAMILY...]] [--json]   the P14 exit from the stored verdicts only (MET, NOT MET or NOT EVALUABLE; exit 0 whatever the verdict); read only
          xd pairs run --from YYYY-MM-DD --to YYYY-MM-DD [--variant E,L]   same-sector cash pairs: form each quarter's pairs on 252 daily closes (every candidate counted in the ledger first), walk them intraday (research only; gross unless Pairs:CostSheetId is set)
          xd pairs eval --family pairs:intraday-1 [--holdout]   protocol S verdict over the stored pair trades (KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE with the missing quantities named)
          xd data eod-probe|eod-load|eod-actions|eod-universe|eod-check ...   NSE daily files (CF-04 Part A; off by default, see ADR 0195): run xd data eod-probe with no dates for its usage
          xd data backfill|probe-seam|history ...   the Dhan 1-minute backfill before each instrument's first CSV bar (CF-04 Part B; off by default, see ADR 0270): run xd data backfill --help for its usage
          xd learn features --run RUNKEY [--run ...]
          xd learn phase-check [--replay-seconds X]
          xd learn decay --strategy ID   the decay monitor, the kill-rule evaluations and the open kill proposals of one strategy (read only; unavailable and not_evaluated are never a pass)
          xd learn loop   is the learning loop helping? the three arms, the learning curve, the reliability slope, the lift (read only; today every section is unavailable and says why)
          xd learn curve   the learning-curve part of that report (read only)
          xd learn influence MODEL-ID   whether the model may influence a decision, term by term with the reasons, the hold-out state, the champion flag and the three ML switches (read only; unavailable and refused are never a pass)
          xd learn rollback   the rollback proposals, whether ml.auto_rollback is ON or OFF the champion now and the proposals that leave no qualifying previous champion (read only; applying a rollback stays the owner's act)
          xd readiness [system]   the system readiness card and one line per Lab strategy, weakest first, with the enforce setting (read only; unavailable is a fail and never a pass)
          xd readiness STRATEGY [--version N]   the readiness scorecard of one strategy version: the score as the minimum of the sub-scores, the label, hard fails, the 15-row checklist with each input, gates R and S, the snapshot, the settings hash and the enforce setting (read only)
          xd select cells --date YYYY-MM-DD [--min-state candidate|paper|active]
          xd plan run [--date YYYY-MM-DD] [--dry-run]
          xd plan phase-check [--through YYYY-MM-DD]
          xd exec phase-check [--account ID] [--through YYYY-MM-DD]
          xd events phase-check [--cut FAMILY[,FAMILY...]] [--json]
          xd plan digest [--week YYYY-Www] [--store]
          xd learn training-set --run RUNKEY:STYLE:EXIT [--run ...] [--require NAME,NAME,...] [--half-life-days N] [--prior-strength N] [--min-trades N]
          xd live compliance show [--kind static_ip|sebi_recheck|dhan_ip_read_policy] [--limit 1..200]
          xd live capture [--account ID] [--date YYYY-MM-DD]
          xd live outcomes [--account ID] [--date YYYY-MM-DD]
          xd live estimates [--week YYYY-MM-DD]
          xd live waterfall --entry ID
          xd live report --account ID [--from YYYY-MM-DD] [--to YYYY-MM-DD]
          xd live ramp show [--account ID]
          xd live phase-check [--through YYYY-MM-DD] [--account ID]
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
