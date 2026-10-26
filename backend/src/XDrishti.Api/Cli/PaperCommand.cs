using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;

namespace XDrishti.Api.Cli;

/// <summary><c>xd paper capture</c> (spec P10-01, ADR 0116; the epoch verbs are in <see cref="PaperGovernanceCommand"/>): call the use cases. No decision lives here; nothing is an order.</summary>
internal static class PaperCommand
{
    public const string Usage = """
        usage:
          xd paper capture [--date YYYY-MM-DD]
            replays every armed ticket of closed sessions on the stored bars with the grading rules and records one paper trade per account at your size
            without --date: every session of the epoch without a finished capture (newest 30 with work); a session is refused until it has closed
            the record is labelled 'simulated on the official 1-minute bars; no order was placed or exists'; nothing is approved, no ticket changes state
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (args.Count >= 1 && args[0] == "capture")
        {
            return await CaptureAsync([.. args.Skip(1)], services, console, cancellationToken);
        }

        await console.Error.WriteAsync(Usage);
        return XdCommand.UsageError;
    }

    private static async Task<int> CaptureAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = PlanGradeOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        return await GuardedAsync(console, async () =>
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CapturePaperFillsCommand, PaperCaptureOutcome>>()
                .HandleAsync(new CapturePaperFillsCommand(options.Date, true), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteLineAsync(result.Value.Summary);
            return result.Value.FailedSessions > 0 ? XdCommand.Failed : XdCommand.Done;
        }, cancellationToken);
    }

    private static async Task<int> GuardedAsync(SimulateConsole console, Func<Task<int>> run, CancellationToken cancellationToken)
    {
        try
        {
            return await run();
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
}
