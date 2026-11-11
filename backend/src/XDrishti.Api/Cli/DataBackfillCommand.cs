using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.History;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd data backfill|probe-seam|history</c> (CF-04 Part B): the owner's verbs for the Dhan 1-minute backfill. A host step that parses arguments and calls the use cases; no decision lives here. <c>backfill</c> and <c>probe-seam</c> make requests only while
/// <c>Data:History:Backfill:Enabled</c> is on (else they are refused and nothing is sent); <c>backfill --dry-run</c> plans, checks the calendar and the disk, prints the estimate and requests nothing; <c>history</c> only reads. Exit codes: 0 done, 1 refused,
/// 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class DataBackfillCommand
{
    public const string Usage = """
        usage:
          xd data backfill [--instrument ID] [--dry-run]   fetch Dhan's 1-minute bars for the years before each instrument's first CSV bar (needs Data:History:Backfill:Enabled); resumable; --dry-run plans and estimates only
          xd data probe-seam [--instrument ID]              compare Dhan's bars with the CSV bars inside the CSV span, in memory (same switch); a failed probe makes the instrument seam_unresolved
          xd data history                                   how far back each tracked instrument reaches and the state of its backfill
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
            switch (parsed.Verb)
            {
                case "backfill":
                    return await Report(await sp.GetRequiredService<ICommandHandler<RunBackfillCommand, BackfillSummary>>().HandleAsync(new RunBackfillCommand(parsed.InstrumentId, parsed.DryRun), cancellationToken), r => [r.Text], console);
                case "probe-seam":
                    return await Report(await sp.GetRequiredService<ICommandHandler<ProbeSeamCommand, ProbeSeamSummary>>().HandleAsync(new ProbeSeamCommand(parsed.InstrumentId), cancellationToken), r => [.. r.Lines, r.Text], console);
                default:
                    var rows = await sp.GetRequiredService<IQueryHandler<ShowHistoryQuery, IReadOnlyList<HistoryRow>>>().HandleAsync(new ShowHistoryQuery(), cancellationToken);
                    await console.Out.WriteLineAsync($"{rows.Count} instruments (symbol, first CSV bar, status, chunks done/failed/total, probe)");
                    foreach (var row in rows)
                    {
                        await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{row.Symbol} {(row.FirstCsvBar is { } first ? first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-")} {row.Status} {row.ChunksDone}/{row.ChunksFailed}/{row.ChunksTotal} {row.ProbeStatus}"));
                    }

                    return XdCommand.Done;
            }
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
        var verb = args.Count > 0 ? args[0] : string.Empty;
        if (verb is not ("backfill" or "probe-seam" or "history"))
        {
            return (null, ["name a verb: backfill, probe-seam or history"]);
        }

        var errors = new List<string>();
        Guid? instrument = null;
        var dryRun = false;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--instrument" when verb is "backfill" or "probe-seam" && i + 1 < args.Count:
                    if (Guid.TryParse(args[++i], out var id))
                    {
                        instrument = id;
                    }
                    else
                    {
                        errors.Add("--instrument is an instrument id (a GUID)");
                    }

                    break;
                case "--dry-run" when verb == "backfill":
                    dryRun = true;
                    break;
                default:
                    errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        return errors.Count > 0 ? (null, errors) : (new Parsed(verb, instrument, dryRun), errors);
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

    internal sealed record Parsed(string Verb, Guid? InstrumentId, bool DryRun);
}
