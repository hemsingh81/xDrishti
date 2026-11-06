using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn decay</c> (spec CF-12, part 12c-1, ADR 0218 and 0219): prints the decay monitor of one strategy and its open kill proposals by calling the two use cases the API calls. Read only; it proposes, applies and changes nothing, and no decision lives here.</summary>
internal static class LearnDecayCommand
{
    private const int OpenProposalLimit = 50;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnDecayOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnDecayOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var report = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetDecayReportQuery, Result<DecayReportView>>>()
                .HandleAsync(new GetDecayReportQuery(options.Strategy), cancellationToken);
            if (report.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(report.Error.Code + ": " + report.Error.Message));
                return XdCommand.Refused;
            }

            var open = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>>>()
                .HandleAsync(new GetKillProposalsQuery("proposed", report.Value.Strategy, OpenProposalLimit), cancellationToken);
            foreach (var line in LearnReportText.Fit(Lines(report.Value, open)))
            {
                await console.Out.WriteLineAsync(line);
            }

            return report.Value.HasSeries ? XdCommand.Done : XdCommand.Refused;
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

    internal static IEnumerable<string> Lines(DecayReportView v, Result<KillProposalsView> open)
    {
        foreach (var line in LearnReportText.Wrap(v.Line))
        {
            yield return line;
        }

        var epoch = v.Epoch is { } e ? e.ToString(CultureInfo.InvariantCulture) : "none";
        foreach (var line in LearnReportText.Wrap(string.Create(CultureInfo.InvariantCulture, $"strategy {v.Strategy}; paper epoch {epoch}; window {v.Settings.WindowTrades} trades, alert {v.Settings.Sigmas} standard errors below the reference; same-day correlation {v.Settings.Rho}{(v.Settings.RhoIsPlaceholder ? " (a placeholder until CF-01 estimates it)" : string.Empty)}; settings {v.Settings.SettingsHash} ({v.Settings.Settings})")))
        {
            yield return line;
        }

        foreach (var line in Reference(v.Reference))
        {
            yield return line;
        }

        foreach (var line in Series("edge series (graded shadow trades, the primary reading)", v.Edge))
        {
            yield return line;
        }

        foreach (var line in Series("behavioural series (P10 paper trades, beside it)", v.Behavioural))
        {
            yield return line;
        }

        yield return "kill rules (evaluations only; a firing rule is proposed by the daily run, never by this verb):";
        foreach (var k in v.Kill)
        {
            foreach (var line in Kill(k))
            {
                yield return line;
            }
        }

        foreach (var line in Proposals(open))
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

    private static IEnumerable<string> Reference(DecayReferenceView r)
    {
        if (r.State != "available")
        {
            foreach (var line in LearnReportText.Wrap("unavailable: " + (r.Reason ?? "no reason given") + ". A series with no reference is not judged; that is not a pass.", "reference: "))
            {
                yield return line;
            }

            yield break;
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"reference: {r.Trades} backtest trades (N_ref), mean {LearnReportText.Figure(r.MeanR)} R (mu_ref), sigma {LearnReportText.Figure(r.Sigma)}, {r.Basis} of costs");
    }

    private static IEnumerable<string> Series(string name, DecaySeriesView s)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"{name}: {s.State}, {s.Trades} trade(s), {s.PendingTrades} after the last completed window");
        foreach (var line in LearnReportText.Wrap(s.Basis, "  basis: "))
        {
            yield return line;
        }

        if (s.Note is not null)
        {
            foreach (var line in LearnReportText.Wrap(s.Note, "  note: "))
            {
                yield return line;
            }
        }

        if (s.State is "unavailable" or "no_series")
        {
            yield return "  no window is judged; this is not a pass";
            yield break;
        }

        if (s.Windows.Count == 0)
        {
            yield return "  no completed window yet; nothing is judged";
        }

        foreach (var w in s.Windows)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  window {w.Index}: {w.FirstDay:yyyy-MM-dd} to {w.LastDay:yyyy-MM-dd}, {w.Trades} trades, mean {w.MeanR:0.0000} R, alert level {w.Threshold:0.0000} (design effect {w.DesignEffect:0.00}): {(w.Below ? "BELOW (alert)" : "above")}");
        }

        if (s.Windows.Count > 0)
        {
            var first = s.FirstTwiceAtWindow is { } f ? string.Create(CultureInfo.InvariantCulture, $" (first reached at window {f})") : string.Empty;
            yield return string.Create(CultureInfo.InvariantCulture, $"  below in a row at the end: {s.Streak}; twice in a row: {(s.TwiceInARow ? "yes" : "no")}{first}");
        }

        if (s.Cusum is { } c)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  CUSUM: {(c.Alarm ? "ALARM" : "no alarm")}, {c.Events} event(s)");
            if (c.Latest is not null)
            {
                foreach (var line in LearnReportText.Wrap(c.Latest, "    latest: "))
                {
                    yield return line;
                }
            }
        }

        foreach (var q in s.Quarters)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  {q.Label}: {q.Trades} trade(s), mean {q.MeanR:0.0000} R{(q.Weak ? ", weak" : string.Empty)}");
        }

        if (s.WeakQuarterShare is { } share)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  share of quarters below the floor: {share:0.##}");
        }
    }

    private static IEnumerable<string> Kill(DecayKillView k)
    {
        var meaning = k.State switch
        {
            "fires" => "FIRES",
            "holds" => "holds",
            "held" => "HELD (fires on the numbers but waits for new trades; not a pass)",
            "not_evaluated" => "NOT EVALUATED (not a pass)",
            _ => k.State,
        };
        var numbers = k.State == "not_evaluated" ? string.Empty : string.Create(CultureInfo.InvariantCulture, $", value {LearnReportText.Figure(k.Value)} against {LearnReportText.Figure(k.Limit)}");
        yield return string.Create(CultureInfo.InvariantCulture, $"  {k.Rule}: {meaning}{numbers}, {k.Trades} trade(s){(k.ProposalOnly ? "; proposal only, the automatic setting never executes it" : string.Empty)}");
        foreach (var line in LearnReportText.Wrap(k.Text, "    "))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Proposals(Result<KillProposalsView> open)
    {
        if (open.IsFailure)
        {
            foreach (var line in LearnReportText.Wrap($"unavailable: {open.Error.Code}: {open.Error.Message}", "open kill proposals: "))
            {
                yield return line;
            }

            yield break;
        }

        var p = open.Value;
        yield return "automatic demotion (confidence.kill.auto): " + (p.Settings.Auto ? "ON (the owner switched it on)" : "OFF (a proposal waits for the owner)");
        if (p.Items.Count == 0)
        {
            yield return "open kill proposals: none";
            yield break;
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"open kill proposals: {p.Items.Count}");
        foreach (var item in p.Items)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  #{item.Id} {item.Rule}{(item.ProposalOnly ? " (proposal only)" : string.Empty)}, proposed {item.ProposedOn:yyyy-MM-dd}, would pause {item.Cells.Count} cell(s): {string.Join(", ", item.Cells.Select(LearnReportText.Clean))}");
            foreach (var line in LearnReportText.Wrap(item.Text, "    "))
            {
                yield return line;
            }
        }
    }
}
