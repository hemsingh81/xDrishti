using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Lists;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd data lists-probe|lists-backfill</c> (DATA-01, ADR 0274): the owner's verbs for NSE's dated F&amp;O-ban files. A host step that parses arguments and calls the use cases. Both make requests only while <c>Data:History:Lists:Enabled</c> is on and the
/// endpoint is on the allow-list (else refused, nothing sent). Exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class DataListsCommand
{
    public const string Usage = """
        usage:
          xd data lists-probe --dates YYYY-MM-DD[,YYYY-MM-DD...]            ask NSE for at most 5 dated ban files, store nothing, print status and parse result (needs Data:History:Lists:Enabled and the allow-list entry of ADR 0274)
          xd data lists-backfill --from YYYY-MM-DD --to YYYY-MM-DD [--dry-run]   load the dated ban files of the weekdays not yet stored (same switch); --dry-run stores nothing
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var (parsed, errors) = Parse(args);
        if (parsed is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            if (parsed.Verb == "lists-probe")
            {
                return await Report(await sp.GetRequiredService<ICommandHandler<ListsProbeCommand, ListsProbeReport>>().HandleAsync(new ListsProbeCommand(parsed.Dates), cancellationToken), r => r.Lines, console);
            }

            return await Report(await sp.GetRequiredService<ICommandHandler<ListsBackfillCommand, ListsBackfillSummary>>().HandleAsync(new ListsBackfillCommand(parsed.From!.Value, parsed.To!.Value, parsed.DryRun), cancellationToken), r => [r.Text], console);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }

    internal static (Parsed? Command, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        var verb = args.Count > 0 ? args[0] : string.Empty;
        if (verb is not ("lists-probe" or "lists-backfill"))
        {
            return (null, ["name a verb: lists-probe or lists-backfill"]);
        }

        List<DateOnly> dates = [];
        DateOnly? from = null, to = null;
        var dry = false;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--dates" when verb == "lists-probe" && i + 1 < args.Count:
                    dates.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => Date(d, "--dates", errors)).OfType<DateOnly>());
                    break;
                case "--from" when verb == "lists-backfill" && i + 1 < args.Count:
                    from = Date(args[++i], "--from", errors);
                    break;
                case "--to" when verb == "lists-backfill" && i + 1 < args.Count:
                    to = Date(args[++i], "--to", errors);
                    break;
                case "--dry-run" when verb == "lists-backfill":
                    dry = true;
                    break;
                default:
                    errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        if (errors.Count == 0)
        {
            if (verb == "lists-probe" && dates.Count == 0)
            {
                errors.Add("--dates is required");
            }
            else if (verb == "lists-backfill" && (from is null || to is null))
            {
                errors.Add("--from and --to are required");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new Parsed(verb, dates, from, to, dry), errors);
    }

    private static async Task<int> Report<T>(Result<T> result, Func<T, IReadOnlyList<string>> lines, SimulateConsole console)
    {
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
            return XdCommand.Refused;
        }

        foreach (var line in lines(result.Value))
        {
            await console.Out.WriteLineAsync(line);
        }

        return XdCommand.Done;
    }

    private static DateOnly? Date(string text, string name, List<string> errors)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add($"{name} is a date as YYYY-MM-DD");
        return null;
    }

    internal sealed record Parsed(string Verb, IReadOnlyList<DateOnly> Dates, DateOnly? From, DateOnly? To, bool DryRun);
}
