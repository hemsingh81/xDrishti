using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// The independent record the adversarial property tests share (ISS-13): the existing bucket reference and batch indicator reference, and the rule written from the definition. With D the India date of the minute just fed
/// and S the number of trailing closed buckets of the feature's timeframe whose start date is D, a session feature on an intraday timeframe reads <c>reference[n − 1 − k]</c> when <c>k &lt; S</c> and is NaN (not ready)
/// otherwise — S = 0 is pending at every lag, lag 0 included; every other feature, and every feature on 1D, reads <c>reference[n − 1 − k]</c> whenever that bucket exists.
/// </summary>
internal static class AdversarialReferenceCheck
{
    private static readonly HashSet<Timeframe> IntradayLanes = [.. AdversarialFrames.Intraday];

    /// <summary>
    /// Steps a frame built from <paramref name="specs"/> through <paramref name="bars"/> and checks every handle at every lag after every step against the reference. Each session feature also gets an alias with a
    /// price source the indicator ignores and a repeat of the spec itself (more handles on the same instance), so a feature spelled three ways is checked three times.
    /// </summary>
    public static void Run(List<FeatureSpec> specs, List<Bar> bars, int depth, bool useCalendar, string label, bool addAliases = true) =>
        RunSegments(specs, [bars], depth, useCalendar ? CalendarFixtures.Windows(bars) : null, label, addAliases: addAliases);

    /// <summary>
    /// The same check over several segments fed one after another to one frame with a <see cref="FeatureFrame.Reset"/> between them: every segment is a run of bars (it may start mid-day, mid-session, on any date, and may
    /// replay dates an earlier segment had), and after the reset the frame must be what a new frame is. With <paramref name="skipped"/> a duplicate or an older bar is fed now and then after a bar of the segment: it must
    /// change nothing, and the state is checked again after it against the same reference step.
    /// </summary>
    public static void RunSegments(
        List<FeatureSpec> specs,
        IReadOnlyList<IReadOnlyList<Bar>> segments,
        int depth,
        IReadOnlyDictionary<DateOnly, (int From, int To)>? windows,
        string label,
        Random? skipped = null,
        bool addAliases = true)
    {
        var aliasOf = new Dictionary<int, int>();
        var originalCount = specs.Count;
        specs = [.. specs];
        for (var i = 0; i < originalCount && addAliases; i++)
        {
            if (AdversarialFrames.Twelve.Contains(specs[i].Name))
            {
                // the same feature spelled with a price source the indicator ignores, and the very same spec again: more handles, the same instance
                aliasOf[specs.Count] = i;
                specs.Add(specs[i] with { Source = PriceSource.High });
                aliasOf[specs.Count] = i;
                specs.Add(specs[i]);
            }
        }

        var frame = AdversarialFrames.Create(specs, depth, windows is null ? null : CalendarFixtures.For(windows));
        var restarting = specs.Select(s => AdversarialFrames.Twelve.Contains(s.Name) && IntradayLanes.Contains(s.Timeframe)).ToArray();
        var timeframes = specs.Select(s => s.Timeframe).Distinct().ToList();
        for (var segment = 0; segment < segments.Count; segment++)
        {
            var bars = segments[segment];
            var skippedInSegment = 0L;
            if (segment > 0)
            {
                frame.Reset();
                frame.Steps.ShouldBe(0, label + ": Steps after Reset");
                frame.IgnoredBars.ShouldBe(0, label + ": IgnoredBars after Reset");
            }

            var closedByStep = timeframes.ToDictionary(t => t, t => FrameReference.ClosedByStep([.. bars], t, windows));
            var closedBars = timeframes.ToDictionary(t => t, t => closedByStep[t].SelectMany(x => x).Select(b => b.AsBar).ToList());
            var closedDates = timeframes.ToDictionary(t => t, t => closedByStep[t].SelectMany(x => x).Select(b => SessionClock.DateOf(b.Start)).ToList());
            var reference = new List<double[]>();
            for (var h = 0; h < specs.Count; h++)
            {
                reference.Add(aliasOf.TryGetValue(h, out var original) ? reference[original] : ReferenceIndicators.Compute(specs[h], closedBars[specs[h].Timeframe]));
            }

            var closedSoFar = timeframes.ToDictionary(t => t, _ => 0);
            var inSession = timeframes.ToDictionary(t => t, _ => 0);
            for (var t = 0; t < bars.Count; t++)
            {
                frame.Step(bars[t]);
                var date = SessionClock.DateOf(bars[t].EpochSeconds);
                foreach (var timeframe in timeframes)
                {
                    closedSoFar[timeframe] += closedByStep[timeframe][t].Count;
                    var dates = closedDates[timeframe];
                    var count = 0;
                    while (count < closedSoFar[timeframe] && dates[closedSoFar[timeframe] - 1 - count] == date)
                    {
                        count++;
                    }

                    inSession[timeframe] = count;
                    if (frame.ClosedBarsAt(timeframe) != closedByStep[timeframe][t].Count)
                    {
                        Assert.Fail($"{label}: segment {segment} step {t} ({AdversarialFrames.StepName(bars[t])}) {timeframe.ToCode()}: the lane closed {frame.ClosedBarsAt(timeframe)} bar(s), the reference {closedByStep[timeframe][t].Count}");
                    }
                }

                // every lag at every step when the history is short; with a deep history every lag at the steps around a date change and at one step in nine, and the first sixteen lags and a sample of the deeper ones at the rest
                var around = t < 3 || SessionClock.DateOf(bars[t - 3].EpochSeconds) != date;
                CheckAll(frame, specs, reference, restarting, closedSoFar, inSession, depth, depth <= 16 || around || t % 9 == 0, $"{label}: segment {segment} step {t} ({AdversarialFrames.StepName(bars[t])})");

                if (skipped is not null && t > 0 && skipped.Next(100) < 4)
                {
                    // a duplicate of this bar, or the one before it: both are at or before the last accepted bar, so they are skipped by every lane
                    frame.Step(skipped.Next(2) == 0 ? bars[t] : bars[t - 1]);
                    skippedInSegment++;
                    frame.IgnoredBars.ShouldBe(skippedInSegment, label + ": IgnoredBars counts the skipped bars");
                    CheckAll(frame, specs, reference, restarting, closedSoFar, inSession, depth, true, $"{label}: segment {segment} after a skipped bar following step {t} ({AdversarialFrames.StepName(bars[t])})");
                }
            }
        }
    }

    private static void CheckAll(
        FeatureFrame frame,
        List<FeatureSpec> specs,
        List<double[]> reference,
        bool[] restarting,
        Dictionary<Timeframe, int> closedSoFar,
        Dictionary<Timeframe, int> inSession,
        int depth,
        bool everyLag,
        string where)
    {
        for (var h = 0; h < specs.Count; h++)
        {
            var timeframe = specs[h].Timeframe;
            for (var k = 0; k < depth; k++)
            {
                if (!everyLag && k > 15 && k % 17 != 0 && k != depth - 1)
                {
                    continue;
                }

                var index = closedSoFar[timeframe] - 1 - k;
                var want = index < 0 || (restarting[h] && k >= inSession[timeframe]) ? double.NaN : reference[h][index];
                var got = frame.At(h, k);
                var ready = k == 0 ? frame.IsReady(h) : frame.IsReadyAt(h, k);
                if (!Tolerance.Same(want, got) || ready == double.IsNaN(want))
                {
                    Assert.Fail($"{where}: {TestSpecs.Describe(specs[h])} depth {depth} lag {k}: S = {inSession[timeframe]}, expected {want:R}, At {got:R}, ready {ready}");
                }
            }

            if (!Tolerance.Identical(frame[h], frame.At(h, 0)))
            {
                Assert.Fail($"{where}: {TestSpecs.Describe(specs[h])}: this[h] is {frame[h]:R} but At(h, 0) is {frame.At(h, 0):R}");
            }
        }
    }
}
