using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Filings;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd filings extract --from YYYY-MM-DD --to YYYY-MM-DD [--limit N]</c> (spec P15-08): the owner's act that classifies stored announcements with the local model, one request at a time, resumable. A host step that parses the arguments and calls the use case; no decision lives here and
/// nothing is printed but counts and words (never an announcement's text, subject or the model's reason). It does not switch the assistant on. Exit codes: 0 done, 1 refused (switch off, model not ready), 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class FilingsExtractCommand
{
    public const string Usage = """
        usage:
          xd filings extract --from YYYY-MM-DD --to YYYY-MM-DD [--limit N]
            classifies stored announcements broadcast between the two India dates (inclusive) that have no extraction for the current model, prompt and schema, one request at a time;
            refuses while the assistant's master switch (ai.settings.enabled) is off (assistant.disabled); stops at the first model failure; run it again to resume
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var (command, errors) = Parse(args);
        if (command is null)
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
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ExtractAnnouncementsCommand, ExtractionSummary>>().HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var line in result.Value.Lines())
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

    internal static (ExtractAnnouncementsCommand? Command, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        DateOnly? from = null, to = null;
        int? limit = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Count:
                    from = Date(args[++i], "--from", errors);
                    break;
                case "--to" when i + 1 < args.Count:
                    to = Date(args[++i], "--to", errors);
                    break;
                case "--limit" when i + 1 < args.Count:
                    if (int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1)
                    {
                        limit = n;
                    }
                    else
                    {
                        errors.Add("--limit is a whole number of at least 1");
                    }

                    break;
                default:
                    errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        if (errors.Count == 0 && (from is null || to is null))
        {
            errors.Add("--from and --to are required");
        }

        return errors.Count > 0 ? (null, errors) : (new ExtractAnnouncementsCommand(from!.Value, to!.Value, limit), errors);
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
