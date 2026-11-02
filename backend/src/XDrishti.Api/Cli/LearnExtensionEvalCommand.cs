using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn extension-eval</c> (spec P15-10): calls the use case and prints each verdict with the quantities it names as missing. No decision lives here. Exit codes follow the other <c>xd</c> commands; a NOT_EVALUABLE verdict is an answer, not a failure (exit 0).</summary>
internal static class LearnExtensionEvalCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnExtensionEvalOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnExtensionEvalOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateExtensionCommand, ExtensionEvaluationOutcome>>()
                .HandleAsync(new EvaluateExtensionCommand(options.Family, options.HoldOut), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var record in result.Value.Records)
            {
                var missing = record.Missing.Count == 0 ? string.Empty : " (" + string.Join("; ", record.Missing.Select(SimulateOptions.Show)) + ")";
                await console.Out.WriteLineAsync($"{result.Value.Family} {record.Protocol} {record.VersionKey}: {record.Verdict}{missing}{(record.HoldOutRead ? " [hold-out read]" : string.Empty)} {record.EvaluationId[..12]}{(record.Stored ? string.Empty : " (already stored; nothing new)")}");
            }

            if (result.Value.Auto is { } auto)
            {
                await console.Out.WriteLineAsync($"event-filter auto rule: {auto.Action} ({auto.Reason})");
            }

            if (result.Value.Note is { } note)
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show(note));
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
}
