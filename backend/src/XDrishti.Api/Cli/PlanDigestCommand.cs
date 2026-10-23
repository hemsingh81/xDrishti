using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd plan digest</c> (spec P7-15, ADR 0094): builds and stores the weekly learning digest by calling the use case. No decision lives here, and nothing is applied.</summary>
internal static class PlanDigestCommand
{
    private const string Actor = "cli:xd";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = PlanDigestOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(PlanDigestOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildDigestCommand, DigestView>>()
                .HandleAsync(new BuildDigestCommand(options.Week, options.DryRun, Actor), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var view = result.Value;
            await console.Out.WriteAsync(view.Text);
            await console.Out.WriteLineAsync(view.Id is null ? "(dry run: nothing stored)" : view.Repeated ? $"(digest {view.Id} was stored already; nothing written)" : $"(stored as digest {view.Id})");
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
