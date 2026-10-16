using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>What a run over a series saw: after every 1-minute bar whether the evaluation timeframe closed a bar, and the three-valued result and the decision of every condition.</summary>
internal sealed record RunLog(bool[] Closed, Truth[][] Truths, bool[][] Decisions)
{
    public static RunLog Empty(int steps, int conditions) =>
        new(new bool[steps], [.. Enumerable.Range(0, steps).Select(_ => new Truth[conditions])], [.. Enumerable.Range(0, steps).Select(_ => new bool[conditions])]);

    /// <summary>Records the conditions as they are now as the answer for step <paramref name="step"/>.</summary>
    public void Read(int step, FeatureFrame frame, IReadOnlyList<BoundCondition> bound)
    {
        for (var k = 0; k < bound.Count; k++)
        {
            Truths[step][k] = bound[k].EvaluateTruth(frame);
            Decisions[step][k] = bound[k].Evaluate(frame);
        }
    }
}

/// <summary>
/// How a series is fed to a frame and read. The look-ahead property is run through a feeder; <see cref="HonestFeeder"/> is the right one, and the two others are deliberately wrong ones that leak the future, to prove
/// that the property can fail.
/// </summary>
internal interface IFeeder
{
    string Name { get; }

    /// <summary>Feeds the bars to a fresh frame and logs what the conditions answered at each step.</summary>
    RunLog Run(Scenario scenario, IReadOnlyList<Bar> bars);
}

/// <summary>The right way: step the frame with a bar, read the conditions, step the next bar.</summary>
internal sealed class HonestFeeder : IFeeder
{
    public string Name => "honest";

    public RunLog Run(Scenario scenario, IReadOnlyList<Bar> bars)
    {
        var (frame, bound) = scenario.NewFrame();
        var log = RunLog.Empty(bars.Count, bound.Count);
        for (var i = 0; i < bars.Count; i++)
        {
            frame.Step(bars[i]);
            log.Closed[i] = frame.BarClosed(scenario.Evaluation);
            log.Read(i, frame, bound);
        }

        return log;
    }
}

/// <summary>
/// Wrong: the answer for step <i>i</i> is read after the frame has also been given bar <i>i + 1</i> (the first bar after the step leaks into it). With the later bars removed the last step has no next bar, so its answer
/// differs from the one it had in the full series.
/// </summary>
internal sealed class NextBarFeeder : IFeeder
{
    public string Name => "reads the first bar after the step";

    public RunLog Run(Scenario scenario, IReadOnlyList<Bar> bars)
    {
        var (frame, bound) = scenario.NewFrame();
        var log = RunLog.Empty(bars.Count, bound.Count);
        for (var i = 0; i < bars.Count; i++)
        {
            frame.Step(bars[i]);
            log.Closed[i] = frame.BarClosed(scenario.Evaluation);
            if (i > 0)
            {
                log.Read(i - 1, frame, bound);
            }
        }

        if (bars.Count > 0)
        {
            log.Read(bars.Count - 1, frame, bound);
        }

        return log;
    }
}

/// <summary>
/// Wrong: the answer for step <i>i</i> is read after the frame has been run on to the minute at which the slowest timeframe of the frame closes its bar — the higher-timeframe bar is read in its final state before it has
/// closed (the bar that is still forming is read as if it had already closed). With the later bars removed that bar never closes in the series, so the answer differs from the one in the full series.
/// </summary>
internal sealed class FormingBarFeeder : IFeeder
{
    public string Name => "reads the higher-timeframe bar before it has closed";

    public RunLog Run(Scenario scenario, IReadOnlyList<Bar> bars)
    {
        var count = bars.Count;
        var (probe, _) = scenario.NewFrame();
        var evaluationClosed = new bool[count];
        var slowClosed = new bool[count];
        for (var i = 0; i < count; i++)
        {
            probe.Step(bars[i]);
            evaluationClosed[i] = probe.BarClosed(scenario.Evaluation);
            slowClosed[i] = probe.BarClosed(scenario.Slowest);
        }

        // the minute at or after each step at which the slowest timeframe closes; the end of the series when it does not
        var target = new int[count];
        var next = count - 1;
        for (var i = count - 1; i >= 0; i--)
        {
            if (slowClosed[i])
            {
                next = i;
            }

            target[i] = next;
        }

        var (frame, bound) = scenario.NewFrame();
        var log = RunLog.Empty(count, bound.Count);
        var position = -1;
        for (var i = 0; i < count; i++)
        {
            while (position < target[i])
            {
                frame.Step(bars[++position]);
            }

            log.Closed[i] = evaluationClosed[i];
            log.Read(i, frame, bound);
        }

        return log;
    }
}

/// <summary>
/// Wrong: the date lane reads the context day dated <i>D</i> itself at a step on <i>D</i> (a same-day close) instead of the latest day strictly before it. Modelled by moving every record of the context one calendar day
/// back, so that the strictly-before rule of the frame lands on the record of the same date. With the records from the cut date on removed or changed the answers on that date differ from the ones of the full series.
/// </summary>
internal sealed class SameDayContextFeeder : IFeeder
{
    public string Name => "reads the context day of the date itself";

    public RunLog Run(Scenario scenario, IReadOnlyList<Bar> bars)
    {
        var shifted = scenario.ContextDays is { } days ? scenario.WithContextDays([.. days.Select(d => d with { Date = d.Date.AddDays(-1) })]) : scenario;
        return new HonestFeeder().Run(shifted, bars);
    }
}
