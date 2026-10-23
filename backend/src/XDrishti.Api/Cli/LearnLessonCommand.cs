using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn lesson</c> (spec P6-16, ADR 0078): the writing side of the lessons, by calling the use cases. No decision lives here. The verb is the owner's, so its moves are the owner's (<c>cli:xd</c>). Exit codes follow the other <c>xd</c> commands.</summary>
internal static class LearnLessonCommand
{
    private const string Actor = "cli:xd";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        try
        {
            var (options, errors) = LearnLessonOptions.Parse(args);
            if (options is null)
            {
                foreach (var error in errors)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(error));
                }

                await console.Error.WriteAsync(LearnLessonOptions.Usage);
                return XdCommand.UsageError;
            }

            await using var scope = services.CreateAsyncScope();
            var p = scope.ServiceProvider;
            return options.Action == "scan" ? await ScanAsync(p, options, console, cancellationToken) : await MoveAsync(p, options, console, cancellationToken);
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

    private static async Task<int> Refused(SimulateConsole console, Error error)
    {
        await console.Error.WriteLineAsync(SimulateOptions.Show(error.Code + ": " + error.Message));
        return XdCommand.Refused;
    }

    private static async Task<int> ScanAsync(IServiceProvider p, LearnLessonOptions o, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<ICommandHandler<OpenLessonsCommand, LessonScanOutcome>>()
            .HandleAsync(new OpenLessonsCommand(o.Sources, new DriftOptions(), new LossTagOptions(), o.MonitorMonths, o.ModelId, o.DryRun, Actor), cancellationToken);
        if (result.IsFailure)
        {
            return await Refused(console, result.Error);
        }

        await console.Out.WriteLineAsync($"{result.Value.Items.Count} findings{(o.DryRun ? " (dry run, nothing written)" : string.Empty)}");
        foreach (var item in result.Value.Items)
        {
            await console.Out.WriteLineAsync(SimulateOptions.Show($"  {item.Status}{(item.LessonId > 0 ? " lesson " + item.LessonId : string.Empty)} {item.Monitor} {item.Subject}: {item.Problem}"));
        }

        foreach (var note in result.Value.NotBuilt)
        {
            await console.Out.WriteLineAsync("not built: " + note);
        }

        return XdCommand.Done;
    }

    private static async Task<int> MoveAsync(IServiceProvider p, LearnLessonOptions o, SimulateConsole console, CancellationToken cancellationToken)
    {
        Result<LessonMoveOutcome> result = o.Action switch
        {
            "link" => await p.GetRequiredService<ICommandHandler<LinkLessonCommand, LessonMoveOutcome>>().HandleAsync(new LinkLessonCommand(o.Id, o.FixKind!, o.FixId, o.Reason, Actor), cancellationToken),
            "close" => await p.GetRequiredService<ICommandHandler<CloseLessonCommand, LessonMoveOutcome>>().HandleAsync(new CloseLessonCommand(o.Id, o.Reason, Actor), cancellationToken),
            _ => await p.GetRequiredService<ICommandHandler<DismissLessonCommand, LessonMoveOutcome>>().HandleAsync(new DismissLessonCommand(o.Id, o.Reason, Actor), cancellationToken),
        };
        if (result.IsFailure)
        {
            return await Refused(console, result.Error);
        }

        await console.Out.WriteLineAsync($"lesson {result.Value.Id}: {result.Value.State}");
        await console.Out.WriteLineAsync(result.Value.Text);
        return XdCommand.Done;
    }
}
