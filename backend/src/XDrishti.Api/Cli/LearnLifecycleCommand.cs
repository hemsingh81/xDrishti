using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn lifecycle</c> (spec P6-08, ADR 0073): records a cell move or reverts a changelog row by calling the use cases. No decision lives here.</summary>
internal static class LearnLifecycleCommand
{
    private const string Actor = "cli:xd";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnLifecycleOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnLifecycleOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = options.RevertId is { } row
                ? await scope.ServiceProvider.GetRequiredService<ICommandHandler<RevertChangelogCommand, ChangelogOutcome>>()
                    .HandleAsync(new RevertChangelogCommand(row, options.Reason, Actor), cancellationToken)
                : await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>>()
                    .HandleAsync(new RecordCellTransitionCommand(options.Cell!, options.To!.Value, options.Reason, Actor, options.Facts, options.Evidence, options.BeforeRef, options.AfterRef), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var o = result.Value;
            await console.Out.WriteLineAsync(SimulateOptions.Show($"row {o.Id}: {o.Subject} {o.From} -> {o.To}"));
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
}
