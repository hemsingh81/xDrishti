using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn loop</c> and <c>xd learn curve</c> (spec CF-12, part 12c-1, ADR 0218): prints "is the learning loop helping?" (or only its learning-curve part) by calling the use case the API calls. Read only; no decision lives here. A section whose input has no source says unavailable and why.</summary>
internal static class LearnLoopCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, bool curveOnly, CancellationToken cancellationToken)
    {
        var errors = LearnLoopOptions.Check(args);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(error);
            }

            await console.Error.WriteAsync(LearnLoopOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetLoopReportQuery, Result<LoopReportView>>>().HandleAsync(new GetLoopReportQuery(), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var line in LearnReportText.Fit(curveOnly ? CurveLines(result.Value) : Lines(result.Value)))
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

    internal static IEnumerable<string> Lines(LoopReportView v)
    {
        foreach (var line in LearnReportText.Wrap(v.Line))
        {
            yield return line;
        }

        foreach (var line in Settings(v.Settings))
        {
            yield return line;
        }

        foreach (var line in Arms(v.Arms, v.Settings.Level))
        {
            yield return line;
        }

        foreach (var line in Curve(v.Curve))
        {
            yield return line;
        }

        foreach (var line in Slope(v.Slope))
        {
            yield return line;
        }

        foreach (var line in Lift(v.Lift))
        {
            yield return line;
        }

        foreach (var line in Statements(v))
        {
            yield return line;
        }
    }

    internal static IEnumerable<string> CurveLines(LoopReportView v)
    {
        foreach (var line in Curve(v.Curve))
        {
            yield return line;
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"settings: flat below {v.Settings.MinSlopePer50Rows:0.####} gain per 50 rows, projection target gain {v.Settings.TargetGain:0.####}; settings {v.Settings.SettingsHash}");
        foreach (var line in Statements(v))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Statements(LoopReportView v)
    {
        foreach (var statement in v.Statements)
        {
            foreach (var line in LearnReportText.Wrap(statement))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> Settings(LoopSettingsView s)
    {
        foreach (var line in LearnReportText.Wrap(string.Create(CultureInfo.InvariantCulture, $"settings {s.SettingsHash} ({s.Settings})")))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Unavailable(string name, string? reason)
    {
        foreach (var line in LearnReportText.Wrap("unavailable: " + (reason ?? "no reason given") + ". Unavailable is not a finding and not a pass.", name + ": "))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Arms(LoopArmsView a, double level)
    {
        if (a.State != "available")
        {
            foreach (var line in Unavailable("three arms", a.Reason))
            {
                yield return line;
            }

            yield break;
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"three arms: {a.Candidates} candidate(s) in the common set; mean value per candidate: frozen {LearnReportText.Figure(a.FrozenMean)}, retrained {LearnReportText.Figure(a.RetrainedMean)}, no model {LearnReportText.Figure(a.NoMlMean)}");
        foreach (var (name, arm) in new[] { ("retrained vs frozen", a.RetrainedVsFrozen), ("retrained vs no model", a.RetrainedVsNoMl) })
        {
            if (arm is null)
            {
                // the report holds no comparison and gave no reason of its own: say so, and that it is not a pass
                foreach (var line in Unavailable("  " + name, "the report holds no comparison for these two arms"))
                {
                    yield return line;
                }

                continue;
            }

            yield return string.Create(CultureInfo.InvariantCulture, $"  {name}: {arm.Verdict}, mean difference {LearnReportText.Figure(arm.MeanDifference)} per candidate, {level * 100:0.##}% interval [{LearnReportText.Figure(arm.Low)}, {LearnReportText.Figure(arm.High)}], P(better) {LearnReportText.Figure(arm.ProbabilityBetter, "0.000")}, P(worse) {LearnReportText.Figure(arm.ProbabilityWorse, "0.000")}, {arm.Candidates} candidate(s) on {arm.Days} day(s)");
            foreach (var line in LearnReportText.Wrap(arm.Text, "    "))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> Curve(LoopCurveView c)
    {
        if (c.State != "available")
        {
            foreach (var line in Unavailable("learning curve", c.Reason))
            {
                yield return line;
            }

            yield break;
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"learning curve: {c.Verdict ?? LearnReportText.Unavailable}, {c.PointsUsed} point(s) fitted, slope {LearnReportText.Figure(c.SlopePer50Rows)} per 50 rows (standard error {LearnReportText.Figure(c.StandardError)}), projected rows {LearnReportText.Figure(c.ProjectedRows, "0.#")}, more rows {LearnReportText.Figure(c.MoreRows, "0.#")}");
        foreach (var line in LearnReportText.Wrap(c.Text, "  "))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Slope(LoopSlopeView s)
    {
        if (s.State != "available")
        {
            foreach (var line in Unavailable("reliability slope", s.Reason))
            {
                yield return line;
            }

            yield break;
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"reliability slope: {s.Verdict ?? LearnReportText.Unavailable}, {s.Outcomes} outcome(s), slope {LearnReportText.Figure(s.Slope)}, intercept {LearnReportText.Figure(s.Intercept)}, standard error {LearnReportText.Figure(s.StandardError)}");
        foreach (var line in LearnReportText.Wrap(s.Text, "  "))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Lift(LoopLiftView l)
    {
        if (l.State != "available")
        {
            foreach (var line in Unavailable("decision-quality lift", l.Reason))
            {
                yield return line;
            }

            yield break;
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"decision-quality lift: top half {l.TopTrades} trade(s), bottom half {l.BottomTrades}, lift {LearnReportText.Figure(l.Lift)} R (standard error {LearnReportText.Figure(l.StandardError)}, z {LearnReportText.Figure(l.LiftZ, "0.00")}); approved tickets in the top half {l.ApprovedInTop} of {l.ApprovedTickets} (z {LearnReportText.Figure(l.ApprovedZ, "0.00")})");
        foreach (var line in LearnReportText.Wrap(l.Text, "  "))
        {
            yield return line;
        }
    }
}
