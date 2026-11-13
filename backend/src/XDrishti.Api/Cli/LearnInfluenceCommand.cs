using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn influence</c> (spec CF-10, part 10d-2a, ADR 0217 and 0220): prints whether one model may influence a decision, term by term, and the three ML switches, by calling the two use cases the API calls. Read only; it records, promotes and switches nothing, and no decision lives here.</summary>
internal static class LearnInfluenceCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnInfluenceOptions.Parse(args);
        if (options is null)
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
            var influence = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetMlInfluenceQuery, Result<MlInfluenceView>>>()
                .HandleAsync(new GetMlInfluenceQuery(options.ModelId), cancellationToken);
            if (influence.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(influence.Error.Code + ": " + influence.Error.Message));
                return XdCommand.Refused;
            }

            // the switches are read by the rollback list's use case (one proposal is enough; only the switches are used)
            var switches = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>>()
                .HandleAsync(new GetRollbackProposalsQuery(null, options.ModelId, 1), cancellationToken);
            foreach (var line in LearnReportText.Fit(Lines(influence.Value, switches)))
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

    internal static IEnumerable<string> Lines(MlInfluenceView v, Result<RollbackProposalsView> switches)
    {
        var champion = v.Role == "champion";
        yield return string.Create(CultureInfo.InvariantCulture, $"influence of model #{v.ModelId}: role {LearnReportText.Clean(v.Role)}, champion: {(champion ? "yes" : "no")}");
        foreach (var line in LearnReportText.Wrap(v.MayInfluence
            ? "verdict: every term holds and the model is the champion, so it may influence a decision (no order is placed by any of this)"
            : "verdict: NOT a pass. The model may not influence any decision; every unavailable, refused or not evaluated term counts against it."))
        {
            yield return line;
        }

        yield return "terms of the predicate:";
        foreach (var t in v.Terms)
        {
            var meaning = t.State switch
            {
                "holds" => "HOLDS",
                "fails" => "FAILS (not a pass)",
                "unavailable" => "UNAVAILABLE (not a pass)",
                _ => LearnReportText.Clean(t.State).ToUpperInvariant() + " (not a pass)",
            };
            yield return $"  {LearnReportText.Clean(t.Name)}: {meaning}";
            foreach (var r in t.Reasons)
            {
                foreach (var line in LearnReportText.Wrap(LearnReportText.Clean(r.Code) + ": " + r.Text, "    "))
                {
                    yield return line;
                }
            }
        }

        if (v.Reasons.Count == 0)
        {
            yield return "reasons it may not influence: none";
        }
        else
        {
            yield return "reasons it may not influence:";
            foreach (var r in v.Reasons)
            {
                foreach (var line in LearnReportText.Wrap(LearnReportText.Clean(r.Code) + ": " + r.Text, "  "))
                {
                    yield return line;
                }
            }
        }

        foreach (var line in HoldOut(v.HoldOut))
        {
            yield return line;
        }

        foreach (var line in Switches(switches))
        {
            yield return line;
        }

        foreach (var line in LearnReportText.Wrap($"thresholds {v.SettingHash} ({v.Settings})"))
        {
            yield return line;
        }

        foreach (var note in v.Notes)
        {
            foreach (var line in LearnReportText.Wrap(note))
            {
                yield return line;
            }
        }
    }

    internal static IEnumerable<string> HoldOut(MlHoldOutView h)
    {
        var block = h.BlockStart is { } b ? b.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
        yield return string.Create(CultureInfo.InvariantCulture, $"hold-out: {LearnReportText.Clean(h.State)} (family {LearnReportText.Clean(h.Family)}, block start {block})");
        if (h.Rows is null || h.ProbabilityBeatsNoMl is null)
        {
            foreach (var line in LearnReportText.Wrap("unavailable: there is no hold-out score for this model, so there are no rows and no probability of beating the best no-ML baseline. " + LearnReportText.UnavailableNote, "  odds: "))
            {
                yield return line;
            }
        }
        else
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  odds: probability of beating the best no-ML baseline {LearnReportText.Figure(h.ProbabilityBeatsNoMl)} on {h.Rows} row(s)");
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  effect of the take rule on realised R: {(h.PolicyEffectR is { } e ? LearnReportText.Figure(e) : "unavailable (not a pass)")}");
        if (h.GainLow is { } low && h.GainHigh is { } high)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  interval: the mean log-loss gain over the {LearnReportText.Clean(h.BaselineName ?? "baseline")} baseline lies in [{LearnReportText.Figure(low)}, {LearnReportText.Figure(high)}] (model {LearnReportText.Figure(h.LogLossModel)}, baseline {LearnReportText.Figure(h.LogLossBaseline)}; the look was taken as {LearnReportText.Clean(h.Mode ?? "unknown")})");
        }
        else
        {
            foreach (var line in LearnReportText.Wrap("unavailable: the model has no stored hold-out figures, or too few trade days for an interval. " + LearnReportText.UnavailableNote, "  interval: "))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> Switches(Result<RollbackProposalsView> switches)
    {
        if (switches.IsFailure)
        {
            foreach (var line in LearnReportText.Wrap($"unavailable: {switches.Error.Code}: {switches.Error.Message.TrimEnd('.')}. {LearnReportText.UnavailableNote}", "ML switches: "))
            {
                yield return line;
            }

            yield break;
        }

        var s = switches.Value.Settings;
        yield return "ML switches (the three safety switches default OFF; turning one on is the owner's act in the app):";
        yield return "  ml.influence.enabled: " + (s.InfluenceEnabled
            ? "ON (a model that meets every other term may influence a decision)"
            : "OFF (no model may influence a decision)");
        yield return "  ml.auto_promote: " + (s.AutoPromote
            ? "ON (the daily run promotes a challenger that meets the promotion rule and the predicate)"
            : "OFF (promotion is the owner's act)");
        yield return "  ml.auto_rollback: " + (s.AutoRollback
            ? "ON (the daily run demotes a champion whose detector fires, recorded as auto)"
            : "OFF (a rollback proposal waits for the owner)");
        yield return "  ml.holdout.auto_score: " + (s.AutoHoldOutScore
            ? "ON (the evening seal service scores the pre-dated champion once per block, recorded as auto; the default of ADR 0282)"
            : "OFF (the owner scores a model on the hold-out: xd learn holdout-score)");
    }
}
