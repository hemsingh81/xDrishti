using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd assistant draft</c> (spec P9-07b): starts a drafting job through the use case, waits for it and prints the draft. Draft only: there is no confirm here, and nothing is stored as a strategy or run.</summary>
internal static class AssistantDraftCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = AssistantDraftOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(AssistantDraftOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var started = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StartDraftCommand, DraftView>>().HandleAsync(new StartDraftCommand(options.Idea, null, DraftErrors.CliActor), cancellationToken);
            if (started.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(started.Error.Code + ": " + started.Error.Message));
                return XdCommand.Refused;
            }

            await services.GetRequiredService<DraftJobs>().WhenIdleAsync().WaitAsync(cancellationToken);
            var read = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetDraftQuery, Result<DraftView>>>().HandleAsync(new GetDraftQuery(started.Value.Id), cancellationToken);
            if (read.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(read.Error.Code + ": " + read.Error.Message));
                return XdCommand.Failed;
            }

            var draft = read.Value;
            await console.Out.WriteLineAsync($"draft {draft.Id}: {draft.Status}{(draft.FailReason is null ? string.Empty : " (" + draft.FailReason + ")")}, {draft.Attempts} model call(s)");
            if (options.Show)
            {
                foreach (var line in draft.Understood)
                {
                    await console.Out.WriteLineAsync("understood: " + SimulateOptions.Show(line));
                }

                foreach (var a in draft.Assumptions)
                {
                    await console.Out.WriteLineAsync($"chose: {SimulateOptions.Show(a.Field)} = {SimulateOptions.Show(a.Value)} ({SimulateOptions.Show(a.Why)})");
                }

                foreach (var line in draft.Unsupported)
                {
                    await console.Out.WriteLineAsync("not in the language: " + SimulateOptions.Show(line));
                }

                foreach (var f in draft.Findings)
                {
                    await console.Out.WriteLineAsync($"finding: {f.Code} {SimulateOptions.Show(f.Detail)}");
                }

                foreach (var p in draft.Problems.Where(p => p.Severity == "error"))
                {
                    await console.Out.WriteLineAsync($"problem: {SimulateOptions.Show(p.Path)} {SimulateOptions.Show(p.Message)}");
                }

                if (draft.Yaml is not null)
                {
                    await console.Out.WriteLineAsync(draft.Yaml);
                }
            }

            await console.Out.WriteLineAsync("(a draft only: nothing was stored as a strategy and no test was started; confirm on the Assistant screen)");
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
