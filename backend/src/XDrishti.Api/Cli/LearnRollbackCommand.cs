using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn rollback</c> (spec CF-10, part 10d-2a, ADR 0220): lists the rollback proposals, the automatic-rollback setting and the models that have no champion to fall back on, by calling the use case the API calls. Read only; applying a rollback stays the owner's act in the app.</summary>
internal static class LearnRollbackCommand
{
    private const int ListLimit = 200;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var errors = LearnRollbackOptions.Check(args);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(error);
            }

            await console.Error.WriteAsync(LearnInfluenceOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>>()
                .HandleAsync(new GetRollbackProposalsQuery(null, null, ListLimit), cancellationToken);
            if (result.IsFailure)
            {
                // a failed read is never "none"
                foreach (var line in LearnReportText.Wrap($"unavailable: {result.Error.Code}: {result.Error.Message.TrimEnd('.')}. {LearnReportText.UnavailableNote}"))
                {
                    await console.Error.WriteLineAsync(line);
                }

                return XdCommand.Refused;
            }

            // the champion as it stands now: the stored facts of a proposal were frozen when it was written, so the list is read beside what is true today (one existing read, no write port)
            var champion = await scope.ServiceProvider.GetRequiredService<IQueryHandler<ListModelsQuery, Result<IReadOnlyList<ModelView>>>>()
                .HandleAsync(new ListModelsQuery("champion", 1), cancellationToken);
            foreach (var line in LearnReportText.Fit(Lines(result.Value, champion)))
            {
                await console.Out.WriteLineAsync(line);
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

    private static IEnumerable<string> Champion(Result<IReadOnlyList<ModelView>> champion)
    {
        if (champion.IsFailure)
        {
            foreach (var line in LearnReportText.Wrap($"unavailable: {champion.Error.Code}: {champion.Error.Message.TrimEnd('.')}. {LearnReportText.UnavailableNote}", "champion now: "))
            {
                yield return line;
            }

            yield break;
        }

        yield return champion.Value.Count == 0
            ? "champion now: none (no model is the champion; the owner names one)"
            : string.Create(CultureInfo.InvariantCulture, $"champion now: model #{champion.Value[0].Id}");
    }

    internal static IEnumerable<string> Lines(RollbackProposalsView v, Result<IReadOnlyList<ModelView>> champion)
    {
        yield return "automatic rollback (ml.auto_rollback): " + (v.Settings.AutoRollback ? "ON (the owner switched it on; the daily run demotes a champion whose detector fires, recorded as auto)" : "OFF (a proposal waits for the owner)");
        if (v.Items.Count == 0)
        {
            yield return "rollback proposals: none";
        }
        else
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"rollback proposals: {v.Items.Count} (newest first)");
            foreach (var p in v.Items)
            {
                foreach (var line in Proposal(p))
                {
                    yield return line;
                }
            }
        }

        foreach (var line in Champion(champion))
        {
            yield return line;
        }

        // a dismissed proposal is closed and leaves the model as it is. The facts of an open or applied one were frozen when it was written, so this is "as of when they were proposed"; each model is listed once, by its newest such proposal
        // (the list is newest first), and open and applied are kept apart
        var bare = v.Items.Where(p => p.State != "dismissed" && (p.PreviousChampionId is null || !p.PreviousQualifies))
            .GroupBy(p => p.ModelId).Select(g => g.First()).ToList();
        yield return "proposals that leave (applied) or would leave (open) no qualifying previous champion, as of when they were proposed:";
        foreach (var (state, label) in new[] { ("applied", "applied (leave none)"), ("proposed", "open (would leave none if applied)") })
        {
            var group = bare.Where(p => p.State == state).ToList();
            yield return "  " + label + (group.Count == 0 ? ": none among the proposals shown" : ":");
            foreach (var p in group)
            {
                var why = p.PreviousChampionId is null ? "it replaced no earlier champion" : $"the earlier champion #{p.PreviousChampionId} did not qualify when it was proposed";
                yield return string.Create(CultureInfo.InvariantCulture, $"    model #{p.ModelId} (newest proposal #{p.Id}): {why}");
            }
        }

        yield return "applying a rollback is the owner's act in the app (it moves the champion to challenger); this verb applies, dismisses and changes nothing.";
        foreach (var statement in v.Statements)
        {
            foreach (var line in LearnReportText.Wrap(statement))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> Proposal(RollbackProposalView p)
    {
        var numbers = p.Value is null || p.Limit is null
            ? "numbers unavailable (not a finding)"
            : string.Create(CultureInfo.InvariantCulture, $"value {LearnReportText.Figure(p.Value)} against {LearnReportText.Figure(p.Limit)}");
        yield return string.Create(CultureInfo.InvariantCulture, $"  #{p.Id} model #{p.ModelId}, trigger {LearnReportText.Clean(p.Trigger)}: {numbers}; {LearnReportText.Clean(p.State).ToUpperInvariant()}");
        yield return string.Create(CultureInfo.InvariantCulture, $"    proposed {p.ProposedOn:yyyy-MM-dd} ({p.ProposedAt:yyyy-MM-dd HH:mm}Z); actor {(p.Actor is { } a ? LearnReportText.Clean(a) : "none yet")}; decided {(p.DecidedAt is { } d ? d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z" : "not yet")}{(p.AutoSettingId is { } s ? $"; automatic under setting row {s}" : string.Empty)}");
        foreach (var line in LearnReportText.Wrap(p.Text, "    "))
        {
            yield return line;
        }

        if (p.Cells.Count > 0)
        {
            foreach (var line in LearnReportText.Wrap("cells in alarm: " + string.Join(", ", p.Cells.Select(LearnReportText.Clean)), "    "))
            {
                yield return line;
            }
        }

        yield return "    previous champion: " + (p.PreviousChampionId is { } id
            ? string.Create(CultureInfo.InvariantCulture, $"#{id}, {(p.PreviousQualifies ? "still qualified when proposed (information only; never re-promoted automatically)" : "did not qualify when proposed")}")
            : "none (no champion fallback)");
    }
}
