using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd readiness</c> (spec CF-11, part 2a, ADR 0221): prints the readiness scorecard of a strategy version or of the system by calling the use cases the API calls. Read only; it stores, moves, switches and orders nothing, and no decision lives here. An input that is
/// unavailable prints the word with its reason and counts as a fail, exactly as in the scorecard; a failed read is never shown as a pass nor as an empty result.
/// </summary>
internal static class ReadinessCliCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = ReadinessCliOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(error);
            }

            await console.Error.WriteAsync(ReadinessCliOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            IEnumerable<string> lines;
            if (options.Strategy is null)
            {
                var list = await scope.ServiceProvider.GetRequiredService<IQueryHandler<ListScorecardsQuery, Result<ScorecardListView>>>().HandleAsync(new ListScorecardsQuery(), cancellationToken);
                if (list.IsFailure)
                {
                    return await RefusedAsync(console, list.Error);
                }

                lines = SystemLines(list.Value);
            }
            else
            {
                var version = options.Version;
                if (version is null)
                {
                    var detail = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetStrategyQuery, Result<StrategyDetail>>>().HandleAsync(new GetStrategyQuery(options.Strategy), cancellationToken);
                    if (detail.IsFailure)
                    {
                        return await RefusedAsync(console, detail.Error);
                    }

                    if (detail.Value.LatestVersion < 1)
                    {
                        await console.Error.WriteLineAsync(SimulateOptions.Show($"the strategy {options.Strategy} has no version in the Lab, so there is no scorecard"));
                        return XdCommand.Refused;
                    }

                    version = detail.Value.LatestVersion;
                }

                var card = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetScorecardQuery, Result<ScorecardView>>>().HandleAsync(new GetScorecardQuery(options.Strategy, version.Value), cancellationToken);
                if (card.IsFailure)
                {
                    return await RefusedAsync(console, card.Error);
                }

                lines = CardLines(card.Value);
            }

            foreach (var line in LearnReportText.Fit(lines))
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

    private static async Task<int> RefusedAsync(SimulateConsole console, Error error)
    {
        await console.Error.WriteLineAsync(SimulateOptions.Show(error.Code + ": " + error.Message));
        return XdCommand.Refused;
    }

    internal static IEnumerable<string> SystemLines(ScorecardListView v)
    {
        var s = v.System;
        yield return "readiness of the system (derived on every read; no order is placed by any of this)";
        yield return string.Create(CultureInfo.InvariantCulture, $"system score: {s.Score:0.0} of 100 = min(integrity, weighted mean of the strategy scores); label: {LearnReportText.Clean(s.LabelText)}");
        yield return string.Create(CultureInfo.InvariantCulture, $"  integrity: {s.Integrity:0} (100 when the integrity checks are clear; 0 when one fails or cannot be read: unavailable is not a pass)");
        yield return string.Create(CultureInfo.InvariantCulture, $"  weighted mean of {s.Strategies} strateg{(s.Strategies == 1 ? "y" : "ies")}: {s.WeightedMean:0.0}; weights: {(s.EqualWeights ? "equal (the plan exposes no planned risk share per strategy)" : "the planned risk shares")}");
        yield return s.WeakestStrategy is null
            ? "  weakest strategy: none"
            : "  weakest strategy: " + LearnReportText.Clean(s.WeakestStrategy) + (s.WeakestSubScore is null ? string.Empty : ", weakest sub-score: " + LearnReportText.Clean(s.WeakestSubScore));
        if (s.Reason is not null)
        {
            foreach (var line in LearnReportText.Wrap("why it is 0 or low apart from the strategies: " + s.Reason))
            {
                yield return "  " + line;
            }
        }

        if (v.Strategies.Count == 0)
        {
            yield return "strategies: none in the Lab yet";
        }
        else
        {
            yield return "strategies (weakest first):";
            foreach (var r in v.Strategies)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {LearnReportText.Clean(r.Strategy)} v{(r.Version is { } ver ? ver.ToString(CultureInfo.InvariantCulture) : "none")}: score {r.Score:0.0}, {LearnReportText.Clean(r.LabelText)}, rows passed {r.RowsPassed} of 15, gate R {(r.GateR ? "passes" : "does not pass")}, gate S {(r.GateS ? "passes" : "does not pass")}, weakest sub-score {(r.WeakestSubScore is null ? "none" : LearnReportText.Clean(r.WeakestSubScore))}, hard fails {r.HardFails.Count}");
            }

            yield return "  (xd readiness STRATEGY prints the whole card of one)";
        }

        var row = v.Settings.SwitchId is { } id ? string.Create(CultureInfo.InvariantCulture, $" (switch row {id})") : " (no row is stored: the default)";
        foreach (var line in Enforce(v.Settings.Enforce, row))
        {
            yield return line;
        }

        foreach (var line in LearnReportText.Wrap($"thresholds {v.Settings.SettingsHash} ({v.Settings.Settings})"))
        {
            yield return line;
        }

        foreach (var statement in v.Statements)
        {
            foreach (var line in LearnReportText.Wrap(statement))
            {
                yield return line;
            }
        }
    }

    internal static IEnumerable<string> CardLines(ScorecardView c)
    {
        yield return $"readiness of {LearnReportText.Clean(c.Strategy)} version {(c.Version is { } v ? v.ToString(CultureInfo.InvariantCulture) : "none")} (derived on every read from stored results; no order is placed by any of this)";
        yield return string.Create(CultureInfo.InvariantCulture, $"score: {c.Score:0.0} of 100 = the minimum of the scored sub-scores, or 0 with a hard fail; label: {LearnReportText.Clean(c.LabelText)}");
        yield return string.Create(CultureInfo.InvariantCulture, $"  the minimum of the sub-scores before the hard fails: {c.RawMinimum:0.0}");
        if (c.HardFails.Count == 0)
        {
            yield return "hard fails: none";
        }
        else
        {
            yield return "hard fails (each turns the score into 0; an input that is unavailable is one):";
            foreach (var h in c.HardFails)
            {
                foreach (var line in LearnReportText.Wrap(h, "  - ", "    "))
                {
                    yield return line;
                }
            }
        }

        yield return "sub-scores:";
        foreach (var s in c.SubScores)
        {
            foreach (var line in SubScore(s))
            {
                yield return line;
            }
        }

        if (c.WeakestSubScore is not null)
        {
            foreach (var line in LearnReportText.Wrap("weakest link: " + LearnReportText.Clean(c.WeakestSubScore) + (c.WhatWouldMoveIt is null ? string.Empty : "; what would move it: " + c.WhatWouldMoveIt)))
            {
                yield return line;
            }
        }

        if (c.NotApplicable.Count > 0)
        {
            yield return "left out because they do not apply:";
            foreach (var n in c.NotApplicable)
            {
                foreach (var line in LearnReportText.Wrap(n, "  - ", "    "))
                {
                    yield return line;
                }
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"edge checklist ({c.RowsPassed} of {c.Rows.Count} rows pass):");
        foreach (var r in c.Rows)
        {
            foreach (var line in Row(r))
            {
                yield return line;
            }
        }

        yield return Gate("R (Candidate to Paper, and the owner's re-enable): rows 1 to 12 and 14 all pass", c.GateR);
        foreach (var line in GateRows(c.GateR))
        {
            yield return line;
        }

        yield return Gate("S (Paper to Active): gate R, rows 13 and 15, and the label Ready for the next phase", c.GateS);
        foreach (var line in GateRows(c.GateS))
        {
            yield return line;
        }

        if (c.Waiting.Count > 0)
        {
            yield return "waiting for (each is unavailable, which counts as a fail):";
            foreach (var w in c.Waiting)
            {
                foreach (var line in LearnReportText.Wrap(w, "  - ", "    "))
                {
                    yield return line;
                }
            }
        }

        yield return c.History.Count == 0
            ? "snapshot: no daily snapshot is stored yet (the readiness-snapshot service stores one a day once it is switched on); a gate use stores its own, cited on the changelog row of the move"
            : string.Create(CultureInfo.InvariantCulture, $"snapshot: newest daily snapshot #{c.History[0].Id} of {c.History[0].Date:yyyy-MM-dd}, score {c.History[0].Score:0.0} ({LearnReportText.Clean(c.History[0].Label)}), settings {LearnReportText.Clean(c.History[0].SettingsHash)}; {c.History.Count} point(s) of the trend are stored");
        foreach (var line in Enforce(c.Enforce, string.Empty))
        {
            yield return line;
        }

        foreach (var line in LearnReportText.Wrap($"settings hash {c.SettingsHash} ({c.Settings})"))
        {
            yield return line;
        }

        foreach (var note in c.Notes)
        {
            foreach (var line in LearnReportText.Wrap(note))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> SubScore(SubScoreView s)
    {
        var name = LearnReportText.Clean(s.Name);
        switch (s.State)
        {
            case "scored":
                foreach (var line in LearnReportText.Wrap($"{name}: {LearnReportText.Figure(s.Score, "0.0")} ({s.Reading}){(s.WhatWouldMoveIt is null ? string.Empty : "; to raise it: " + s.WhatWouldMoveIt)}", "  ", "    "))
                {
                    yield return line;
                }

                break;
            case "not_applicable":
                foreach (var line in LearnReportText.Wrap($"{name}: not applicable ({s.Reading}); left out of the minimum", "  ", "    "))
                {
                    yield return line;
                }

                break;
            default:
                foreach (var line in LearnReportText.Wrap($"{name}: unavailable: {s.Reading.TrimEnd('.')}. {LearnReportText.UnavailableNote} It is a hard fail.", "  ", "    "))
                {
                    yield return line;
                }

                break;
        }
    }

    private static IEnumerable<string> Row(ScorecardRowView r)
    {
        var state = r.Status switch
        {
            "pass" => "PASS",
            "fail" => "FAIL",
            _ => "UNAVAILABLE (counts as a fail)",
        };
        yield return string.Create(CultureInfo.InvariantCulture, $"  {r.Number,2}. {LearnReportText.Clean(r.Name)}: {state}");
        foreach (var line in LearnReportText.Wrap("producer: " + r.Producer + "; threshold: " + r.Threshold, "      "))
        {
            yield return line;
        }

        var value = LearnReportText.Clean(r.Value).Trim().TrimEnd('.');
        foreach (var line in LearnReportText.Wrap(r.Status == "unavailable" ? $"unavailable: {value}. {LearnReportText.UnavailableNote}" : "input: " + value, "      "))
        {
            yield return line;
        }
    }

    private static string Gate(string what, ReadinessGateView g) =>
        $"gate {what}: " + (g.Passed ? "PASSES" : "DOES NOT PASS (not a pass)");

    private static IEnumerable<string> GateRows(ReadinessGateView g)
    {
        if (g.Failing.Count > 0)
        {
            yield return "  stopped by rows: " + string.Join(", ", g.Failing.Select(r => r.Number.ToString(CultureInfo.InvariantCulture) + " (" + r.Status + ")"));
        }

        if (!g.LabelOk)
        {
            yield return "  the label is not Ready for the next phase";
        }
    }

    private static IEnumerable<string> Enforce(bool on, string row)
    {
        foreach (var line in LearnReportText.Wrap(on
            ? $"enforce: ON{row}. A gate that does not pass REFUSES the lifecycle move it judges (Candidate to Paper, the owner's re-enable, Paper to Active) with 409 confidence.gate_failed and the failing rows; an unavailable row blocks."
            : $"enforce: OFF{row}. A gate that does not pass only warns: the move goes on and the changelog row records the gate's snapshot and the failing rows. Turning enforce ON or OFF is the owner's act in the app, with a fresh password."))
        {
            yield return line;
        }
    }
}
