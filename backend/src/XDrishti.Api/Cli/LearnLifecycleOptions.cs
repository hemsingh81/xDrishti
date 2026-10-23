using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn lifecycle</c> (spec P6-08): either a cell move or a revert of a changelog row. Nothing here reads a file or the database.</summary>
internal sealed record LearnLifecycleOptions(string? Cell, CellState? To, long? RevertId, string Reason, IReadOnlyList<string> Evidence, TransitionFacts Facts, string? BeforeRef, string? AfterRef)
{
    public const string Usage = """
        usage:
          xd learn lifecycle --cell KEY --to research|candidate|paper|active|paused|retired --reason TEXT [--evidence ID[,ID...]] [--gates-passed] [--owner-approved] [--within-band] [--recovered] [--paper-days N] [--before-ref TEXT] [--after-ref TEXT]
            records a move of doc 09 section 7 as a changelog row; an illegal move or an unmet gate is refused with the reason (active needs --owner-approved, --within-band and at least 20 paper days)
          xd learn lifecycle --revert ROW --reason TEXT
            reverts the latest move of a cell with a new row; nothing is changed or deleted
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] WithValue = ["--cell", "--to", "--reason", "--evidence", "--paper-days", "--before-ref", "--after-ref", "--revert"];
    private static readonly string[] Flags = ["--gates-passed", "--owner-approved", "--within-band", "--recovered"];

    /// <summary>Parses the arguments after <c>learn lifecycle</c>; the errors are listed when the options are null.</summary>
    public static (LearnLifecycleOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Flags.Contains(arg, StringComparer.Ordinal))
            {
                if (!flags.Add(arg))
                {
                    errors.Add($"'{arg}' is given twice");
                }
            }
            else if (!WithValue.Contains(arg, StringComparer.Ordinal))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
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

        if (!values.TryGetValue("--reason", out var reason) || string.IsNullOrWhiteSpace(reason))
        {
            errors.Add("--reason is required");
        }

        long? revert = null;
        if (values.TryGetValue("--revert", out var revertText))
        {
            if (!long.TryParse(revertText, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 1)
            {
                errors.Add("--revert is the number of a changelog row");
            }
            else
            {
                revert = id;
            }

            if (values.Count > 2 || flags.Count > 0)
            {
                errors.Add("--revert takes only --reason");
            }
        }

        string? cell = null;
        CellState? to = null;
        if (revert is null && !values.ContainsKey("--revert"))
        {
            if (!values.TryGetValue("--cell", out cell) || CellId.TryParse(cell) is null)
            {
                errors.Add("--cell is strategy|timeframe|side|style|exit|regime");
            }

            to = values.TryGetValue("--to", out var toText) ? CellStateExtensions.Parse(toText) : null;
            if (to is null)
            {
                errors.Add("--to is research, candidate, paper, active, paused or retired");
            }
        }

        var days = 0;
        if (values.TryGetValue("--paper-days", out var daysText) && (!int.TryParse(daysText, NumberStyles.None, CultureInfo.InvariantCulture, out days) || days > 100_000))
        {
            errors.Add("--paper-days is 0 or more");
        }

        var evidence = values.TryGetValue("--evidence", out var evidenceText)
            ? (IReadOnlyList<string>)[.. evidenceText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];
        var facts = new TransitionFacts(flags.Contains("--gates-passed"), flags.Contains("--owner-approved"), flags.Contains("--within-band"), flags.Contains("--recovered"), days);
        values.TryGetValue("--before-ref", out var before);
        values.TryGetValue("--after-ref", out var after);
        return errors.Count > 0 ? (null, errors) : (new LearnLifecycleOptions(cell, to, revert, reason!, evidence, facts, before, after), errors);
    }
}
