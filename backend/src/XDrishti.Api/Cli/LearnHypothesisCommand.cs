using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn hypothesis</c> (spec P6-10, ADR 0076): the writing side of the hypothesis loop, by calling the use cases. No decision lives here. The verb is the owner's, so its moves are the owner's (<c>cli:xd</c>). Exit codes follow the other <c>xd</c> commands.</summary>
internal static class LearnHypothesisCommand
{
    private const string Actor = "cli:xd";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        try
        {
            var (options, errors) = LearnHypothesisOptions.Parse(args);
            if (options is null)
            {
                foreach (var error in errors)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(error));
                }

                await console.Error.WriteAsync(LearnHypothesisOptions.Usage);
                return XdCommand.UsageError;
            }

            await using var scope = services.CreateAsyncScope();
            var p = scope.ServiceProvider;
            var outcome = options.Action switch
            {
                "grid" => await GridAsync(p, options, console, cancellationToken),
                "triggers" => await TriggersAsync(p, options, console, cancellationToken),
                _ => await WriteAsync(p, options, console, cancellationToken),
            };
            return outcome;
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

    private static async Task<int> GridAsync(IServiceProvider p, LearnHypothesisOptions o, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<IQueryHandler<GridHypothesesQuery, Result<GridHypothesesView>>>().HandleAsync(new GridHypothesesQuery(o.Baseline!, o.Parameter!, o.Grid), cancellationToken);
        if (result.IsFailure)
        {
            return await Refused(console, result.Error);
        }

        var v = result.Value;
        await console.Out.WriteLineAsync($"{v.Parameter} is {v.Current} in the baseline run; grid values: {(v.Values.Count == 0 ? "none" : string.Join(", ", v.Values))}");
        await console.Out.WriteLineAsync(v.Note);
        return XdCommand.Done;
    }

    private static async Task<int> TriggersAsync(IServiceProvider p, LearnHypothesisOptions o, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<IQueryHandler<EvaluateHypothesisTriggersQuery, Result<HypothesisTriggersView>>>()
            .HandleAsync(new EvaluateHypothesisTriggersQuery(o.Sources, o.Since, o.MinNewTrades, new DriftOptions(), o.MonitorMonths), cancellationToken);
        if (result.IsFailure)
        {
            return await Refused(console, result.Error);
        }

        var v = result.Value;
        await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{v.Triggers.Count} triggers; {v.OpenHypotheses} of {v.MaxOpen} hypothesis slots are open"));
        foreach (var t in v.Triggers)
        {
            await console.Out.WriteLineAsync($"  {t.Kind} {t.CellKey}: {t.Detail}");
        }

        foreach (var note in v.Notes)
        {
            await console.Out.WriteLineAsync("note: " + note);
        }

        return XdCommand.Done;
    }

    private static async Task<int> WriteAsync(IServiceProvider p, LearnHypothesisOptions o, SimulateConsole console, CancellationToken cancellationToken)
    {
        Result<HypothesisMoveOutcome> result = o.Action switch
        {
            "propose" => await p.GetRequiredService<ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome>>()
                .HandleAsync(new ProposeHypothesisCommand(o.Source, o.Cell, o.Knob!, o.Reason, o.Baseline!, o.Grid, Actor), cancellationToken),
            "start" => await p.GetRequiredService<ICommandHandler<StartHypothesisCommand, HypothesisMoveOutcome>>()
                .HandleAsync(new StartHypothesisCommand(o.Id, o.Candidate!, Actor), cancellationToken),
            "judge" => await p.GetRequiredService<ICommandHandler<JudgeHypothesisCommand, HypothesisMoveOutcome>>()
                .HandleAsync(new JudgeHypothesisCommand(o.Id, o.Experiment, o.HoldOutMonths, Actor), cancellationToken),
            _ => await p.GetRequiredService<ICommandHandler<MoveHypothesisCommand, HypothesisMoveOutcome>>()
                .HandleAsync(new MoveHypothesisCommand(o.Id, Enum.Parse<HypothesisState>(o.Action switch { "approve" => "Approved", "reject" => "Rejected", "expire" => "Expired", _ => "Accepted" }), o.Reason, Actor), cancellationToken),
        };
        if (result.IsFailure)
        {
            return await Refused(console, result.Error);
        }

        await console.Out.WriteLineAsync($"hypothesis {result.Value.Id}: {result.Value.State}");
        await console.Out.WriteLineAsync(result.Value.Text);
        return XdCommand.Done;
    }
}
