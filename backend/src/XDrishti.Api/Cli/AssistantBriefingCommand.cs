using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai.Briefing;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd assistant briefing</c> (spec P9-06): prints, or builds and stores, the briefing or weekly review text by calling the use case. No decision lives here, and nothing is applied.</summary>
internal static class AssistantBriefingCommand
{
    private const string Actor = "cli:xd";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = AssistantBriefingOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(AssistantBriefingOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            Result<BriefingView> result = options.Store
                ? await scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildBriefingCommand, BriefingView>>().HandleAsync(new BuildBriefingCommand(options.Kind, options.Subject, options.NewVersion, options.ModelOff, Actor), cancellationToken)
                : await scope.ServiceProvider.GetRequiredService<IQueryHandler<PreviewBriefingQuery, Result<BriefingView>>>().HandleAsync(new PreviewBriefingQuery(options.Kind, options.Subject), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var view = result.Value;
            await console.Out.WriteLineAsync(view.Text);
            await console.Out.WriteLineAsync(view.Id is null
                ? "(printed only: nothing stored; use --store to keep it)"
                : view.Repeated ? $"({view.Kind} {view.Subject} version {view.Version} was stored already; nothing written)" : $"(stored as {view.Kind} {view.Subject} version {view.Version}, source {view.Source}{(view.Reason is null ? string.Empty : ", " + view.Reason)})");
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
