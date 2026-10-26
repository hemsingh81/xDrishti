using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd paper epoch show|restart|explain</c>, <c>xd paper config</c> and <c>xd paper queue</c> (spec P10-05, ADR 0119): call the use cases. The restart and the note are the owner's acts and exist only here (actor <c>cli:xd</c>); nothing applies a
/// hypothesis or changes a setting.
/// </summary>
internal static class PaperGovernanceCommand
{
    public const string Actor = "cli:xd";

    public const string Usage = """
        usage:
          xd paper epoch show [--id N]
            the measurement epochs with their counts, or one epoch with its recorded changes and notes (read only)
          xd paper epoch restart --reason "<10 to 300 characters>"
            starts a new epoch (the count restarts) from the next trading day; nothing is deleted or edited, the earlier epoch keeps its trades
          xd paper epoch explain --change ID [--hypothesis ID] --reason "<10 to 300 characters>"
            attaches your note (and an accepted hypothesis) to a recorded change; it explains, it changes nothing
          xd paper config
            the configuration now with the rule of every component, the last snapshot, the differences not yet captured and the learning services that would restart the count (a preview, stores nothing)
          xd paper queue
            accepted hypotheses nothing has used: accepted means judged better offline; nothing was applied; applying it starts a new epoch
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Whether the arguments after <c>paper</c> are one of these verbs.</summary>
    public static bool Handles(IReadOnlyList<string> args) =>
        args.Count >= 1 && (args[0] is "config" or "queue" || (args[0] == "epoch" && args.Count >= 2 && args[1] is "show" or "restart" or "explain"));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var verb = args[0] == "epoch" ? args[1] : args[0];
        var rest = args.Skip(args[0] == "epoch" ? 2 : 1).ToList();
        var options = Options(rest);
        if (options is null)
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var p = scope.ServiceProvider;
            switch (verb)
            {
                case "show" when options.Keys.All(k => k == "id"):
                    return options.TryGetValue("id", out var id) && long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var epochId)
                        ? await ShowOneAsync(p, epochId, console, cancellationToken)
                        : options.ContainsKey("id") ? await UsageAsync(console) : await ShowAllAsync(p, console, cancellationToken);
                case "restart" when options.Keys.All(k => k == "reason") && options.ContainsKey("reason"):
                    return await RestartAsync(p, options["reason"], console, cancellationToken);
                case "explain" when options.Keys.All(k => k is "change" or "hypothesis" or "reason") && options.ContainsKey("change") && options.ContainsKey("reason"):
                    long? hypothesis = null;
                    if (options.TryGetValue("hypothesis", out var h))
                    {
                        if (!long.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                        {
                            return await UsageAsync(console);
                        }

                        hypothesis = parsed;
                    }

                    return long.TryParse(options["change"], NumberStyles.None, CultureInfo.InvariantCulture, out var change)
                        ? await ExplainAsync(p, change, hypothesis, options["reason"], console, cancellationToken)
                        : await UsageAsync(console);
                case "config" when options.Count == 0:
                    return await ConfigAsync(p, console, cancellationToken);
                case "queue" when options.Count == 0:
                    return await QueueAsync(p, console, cancellationToken);
                default:
                    return await UsageAsync(console);
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

    private static async Task<int> UsageAsync(SimulateConsole console)
    {
        await console.Error.WriteAsync(Usage);
        return XdCommand.UsageError;
    }

    private static Dictionary<string, string>? Options(List<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count || !result.TryAdd(args[i][2..], args[i + 1]))
            {
                return null;
            }
        }

        return result;
    }

    private static string S(string? text) => SimulateOptions.Show(text ?? string.Empty);

    private static string Line(EpochView e) =>
        string.Create(CultureInfo.InvariantCulture, $"epoch {e.Id} ({e.Kind}) {e.StartedOn:yyyy-MM-dd} to {(e.EndedOn is { } end ? end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "now")} fingerprint {e.Fingerprint ?? "-"}: counted {e.Counted}, straddle {e.Straddle}, risk-down changes {e.RiskDownChanges}, unexplained {e.UnexplainedChanges}; {S(e.Reason)}");

    private static async Task<int> ShowAllAsync(IServiceProvider p, SimulateConsole console, CancellationToken cancellationToken)
    {
        var list = (await p.GetRequiredService<IQueryHandler<GetEpochsQuery, Result<EpochList>>>().HandleAsync(new GetEpochsQuery(), cancellationToken)).Value;
        if (list.Items.Count == 0)
        {
            await console.Out.WriteLineAsync("No capture has run yet; the first epoch starts with the first capture.");
        }

        foreach (var e in list.Items)
        {
            await console.Out.WriteLineAsync(Line(e));
        }

        await console.Out.WriteLineAsync("basis: " + list.Basis);
        return XdCommand.Done;
    }

    private static async Task<int> ShowOneAsync(IServiceProvider p, long id, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<IQueryHandler<GetEpochQuery, Result<EpochDetail>>>().HandleAsync(new GetEpochQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return await RefusedAsync(console, result.Error);
        }

        await console.Out.WriteLineAsync(Line(result.Value.Epoch));
        foreach (var c in result.Value.Changes)
        {
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"  change {c.Id} {c.Class} {c.Effect} {S(c.Key)}: {S(c.Before ?? "(added)")} -> {S(c.After ?? "(removed)")}; {(c.ExplainedBy ?? (c.Unexplained ? "unexplained" : "noted by the owner"))}"));
        }

        foreach (var n in result.Value.Notes)
        {
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"  note {n.Id} (change {n.ChangeId}, hypothesis {n.HypothesisId}) by {S(n.Actor)}: {S(n.Text)}"));
        }

        await console.Out.WriteLineAsync("basis: " + result.Value.Basis);
        return XdCommand.Done;
    }

    private static async Task<int> RestartAsync(IServiceProvider p, string reason, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<ICommandHandler<StartEpochCommand, EpochView>>().HandleAsync(new StartEpochCommand(reason, Actor), cancellationToken);
        if (result.IsFailure)
        {
            return await RefusedAsync(console, result.Error);
        }

        await console.Out.WriteLineAsync("started " + Line(result.Value) + " (the earlier epoch keeps its trades)");
        return XdCommand.Done;
    }

    private static async Task<int> ExplainAsync(IServiceProvider p, long change, long? hypothesis, string reason, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<ICommandHandler<ExplainChangeCommand, ExplainOutcome>>().HandleAsync(new ExplainChangeCommand(change, hypothesis, reason, Actor), cancellationToken);
        if (result.IsFailure)
        {
            return await RefusedAsync(console, result.Error);
        }

        await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"noted change {result.Value.ChangeId} of epoch {result.Value.EpochId}; nothing was applied or changed"));
        return XdCommand.Done;
    }

    private static async Task<int> ConfigAsync(IServiceProvider p, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<IQueryHandler<GetConfigQuery, Result<ConfigView>>>().HandleAsync(new GetConfigQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return await RefusedAsync(console, result.Error);
        }

        var v = result.Value;
        await console.Out.WriteLineAsync($"configuration now {v.CurrentHash}; last snapshot {(v.Last is null ? "none yet" : v.Last.Hash + " of " + v.Last.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        foreach (var c in v.Components)
        {
            await console.Out.WriteLineAsync($"  {S(c.Key)} = {S(c.Value)}   [{c.Rule}]");
        }

        foreach (var g in v.Unreadable)
        {
            await console.Out.WriteLineAsync("  UNREADABLE: " + g + " (a capture now would claim no change)");
        }

        foreach (var c in v.Pending)
        {
            await console.Out.WriteLineAsync($"  NOT YET CAPTURED {c.Class}: {S(c.Key)}: {S(c.Before ?? "(added)")} -> {S(c.After ?? "(removed)")}: {c.Meaning}");
        }

        foreach (var b in v.Blockers)
        {
            await console.Out.WriteLineAsync($"  BLOCKER service {b.ServiceId}: {b.Text}");
        }

        await console.Out.WriteLineAsync("basis: " + v.Basis);
        return XdCommand.Done;
    }

    private static async Task<int> QueueAsync(IServiceProvider p, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await p.GetRequiredService<IQueryHandler<GetQueueQuery, Result<QueueView>>>().HandleAsync(new GetQueueQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return await RefusedAsync(console, result.Error);
        }

        await console.Out.WriteLineAsync(result.Value.Items.Count == 0 ? "No accepted hypothesis is waiting." : $"{result.Value.Items.Count} accepted hypothesis(es) waiting:");
        foreach (var i in result.Value.Items)
        {
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"  hypothesis {i.HypothesisId}, {i.AgeDays} day(s): {S(i.CellKey ?? "(no cell)")} {S(i.Knob)} {S(i.From)} -> {S(i.To)}, experiment {i.ExperimentId?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));
        }

        await console.Out.WriteLineAsync(result.Value.Note);
        return XdCommand.Done;
    }

    private static async Task<int> RefusedAsync(SimulateConsole console, Error error)
    {
        await console.Error.WriteLineAsync(S(error.Code + ": " + error.Message));
        return XdCommand.Refused;
    }
}
