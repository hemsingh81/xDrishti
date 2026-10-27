using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai.Proposals;

namespace XDrishti.Api.Cli;

/// <summary><c>xd assistant propose</c> (spec P9-08): runs the proposal job once through the use case and prints the counts and the refusal codes. Proposals only: there is no accept verb here, and nothing is applied.</summary>
internal static class AssistantProposeCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = AssistantProposeOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(AssistantProposeOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<GenerateProposalsCommand, ProposalRunView>>().HandleAsync(new GenerateProposalsCommand(options.Kind, options.Cell, options.DryRun, ProposalErrors.CliActor), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var run = result.Value;
            await console.Out.WriteLineAsync($"proposals: {run.State}{(run.Reason is null ? string.Empty : " (" + run.Reason + ")")}; pack {run.PackCells} cells, {run.PackCandidates} instruments; {run.Read} read, {run.Stored} {(run.DryRun ? "would be stored" : "stored")}, {run.Refused.Count} refused, {run.AutoAccepted} auto-accepted");
            foreach (var refused in run.Refused)
            {
                await console.Out.WriteLineAsync($"refused: {refused.Kind} {refused.Code}");
            }

            foreach (var preview in run.Previews)
            {
                await console.Out.WriteLineAsync("would store: " + SimulateOptions.Show(preview));
            }

            await console.Out.WriteLineAsync("(proposals only: nothing was applied; accept or reject on the Suggestions screen)");
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
