using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd eval announcements sample|import|show</c> (spec P15-07): the owner's verbs for the announcement set of the eval. A host step that reads the label file and calls the use cases; no decision lives here. No model, no network; the sample and the labelled text are written
/// under <c>data/eval</c> only. Exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class AnnouncementsCommand
{
    public const string Usage = """
        usage:
          xd eval announcements sample --from YYYY-MM-DD --to YYYY-MM-DD [--seed 1337]
            draws 240 stored announcements (200 main + 40 reserve; half from the baseline-material categories) and writes data/eval/announcements/to-label.csv (with text) and sample.json (no text)
          xd eval announcements import FILE.csv    checks the labelled file against the sample (ids, unchanged text, labels, 200 definite after the reserve replaces each unsure) and writes data/eval/sets/announcements.jsonl and eval/sets/announcements.manifest.json
          xd eval announcements show               the sample, the class counts, the label distribution per half and the baseline; says so while labels are awaited
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var (verb, from, to, seed, file, errors) = Parse(args);
        if (errors.Count > 0)
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
            Result<AnnouncementEvalReport> result;
            switch (verb)
            {
                case "sample":
                    result = await sp.GetRequiredService<ICommandHandler<SampleAnnouncementsCommand, AnnouncementEvalReport>>().HandleAsync(new SampleAnnouncementsCommand(from!.Value, to!.Value, seed), cancellationToken);
                    break;
                case "import":
                    string labels;
                    try
                    {
                        labels = await File.ReadAllTextAsync(file!, cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        await console.Error.WriteLineAsync("the label file cannot be read");
                        return XdCommand.UsageError;
                    }

                    result = await sp.GetRequiredService<ICommandHandler<ImportAnnouncementLabelsCommand, AnnouncementEvalReport>>().HandleAsync(new ImportAnnouncementLabelsCommand(labels), cancellationToken);
                    break;
                default:
                    result = Result.Success(await sp.GetRequiredService<IQueryHandler<ShowAnnouncementsQuery, AnnouncementEvalReport>>().HandleAsync(new ShowAnnouncementsQuery(), cancellationToken));
                    break;
            }

            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var line in result.Value.Lines)
            {
                await console.Out.WriteLineAsync(line);
            }

            return XdCommand.Done;
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

    private static (string Verb, DateOnly? From, DateOnly? To, long Seed, string? File, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        var verb = args.Count > 0 ? args[0] : string.Empty;
        if (verb is not ("sample" or "import" or "show"))
        {
            return (verb, null, null, 0, null, ["name a verb: sample, import or show"]);
        }

        DateOnly? from = null, to = null;
        long seed = 1337;
        string? file = null;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--from" when verb == "sample" && i + 1 < args.Count:
                    from = Date(args[++i], "--from", errors);
                    break;
                case "--to" when verb == "sample" && i + 1 < args.Count:
                    to = Date(args[++i], "--to", errors);
                    break;
                case "--seed" when verb == "sample" && i + 1 < args.Count:
                    if (!long.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                    {
                        errors.Add("--seed is a whole number");
                    }

                    break;
                default:
                    if (verb == "import" && file is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        file = args[i];
                    }
                    else
                    {
                        errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    }

                    break;
            }
        }

        if (verb == "sample" && (from is null || to is null) && errors.Count == 0)
        {
            errors.Add("--from and --to are required");
        }

        if (verb == "import" && file is null)
        {
            errors.Add("name the labelled csv file");
        }

        return (verb, from, to, seed, file, errors);
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
}
