using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial, the indicators that the frame tells about a new date: "pending" is a transient flag. Whatever mix of bars, resets and pending marks an instance has been through, after an update it reports exactly
/// what a twin that was only fed the bars and the resets reports, a pending instance is not ready with a NaN value, and readiness is always "the value is not NaN". The catalog hands out an instance that can be
/// told for every spec the frame will tell it about.
/// </summary>
public sealed class RestartingIndicatorsAdversarialTests
{
    public static TheoryData<string> Kinds
    {
        get
        {
            var data = new TheoryData<string> { "vwap", "or_high(1)", "or_high(3)", "or_high(15)", "or_low(1)", "or_low(3)", "or_low(15)" };
            foreach (var sessionField in Enum.GetValues<SessionField>())
            {
                data.Add("session:" + sessionField);
            }

            return data;
        }
    }

    private static IIndicator Make(string kind) => kind switch
    {
        "vwap" => new Vwap(),
        "or_high(1)" => new OrHigh(1),
        "or_high(3)" => new OrHigh(3),
        "or_high(15)" => new OrHigh(15),
        "or_low(1)" => new OrLow(1),
        "or_low(3)" => new OrLow(3),
        "or_low(15)" => new OrLow(15),
        _ => new SessionContext(Enum.Parse<SessionField>(kind["session:".Length..])),
    };

    private static void MarkPending(IIndicator indicator) => ((IRestartingIndicator)indicator).MarkPending();

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_pending_flag_leaves_no_trace_after_the_next_update_whatever_the_order_of_updates_resets_and_marks(string kind)
    {
        var bars = SeriesGenerator.Random(13, 2_000, 3_000, specialSessions: true);
        var used = Make(kind);
        var twin = Make(kind);
        var random = new Random(kind.Sum(c => c) + 77);
        var pendingMarks = 0;

        for (var i = 0; i < bars.Count; i++)
        {
            var roll = random.Next(100);
            if (roll < 4)
            {
                used.Reset();
                twin.Reset();
                (used.IsReady == twin.IsReady && used.Value.Equals(twin.Value)).ShouldBeTrue($"{kind}: after Reset at bar {i}");
            }
            else if (roll < 10)
            {
                MarkPending(used);
                pendingMarks++;

                used.IsReady.ShouldBeFalse($"{kind}: pending at bar {i}");
                double.IsNaN(used.Value).ShouldBeTrue($"{kind}: pending at bar {i} has a NaN value");
                if (random.Next(2) == 0)
                {
                    MarkPending(used);

                    used.IsReady.ShouldBeFalse($"{kind}: marked twice at bar {i}");
                }
            }

            used.Update(bars[i]);
            twin.Update(bars[i]);

            if (!(used.IsReady == twin.IsReady && used.Value.Equals(twin.Value)))
            {
                Assert.Fail($"{kind}: after the update of bar {i} ({AdversarialFrames.StepName(bars[i])}) the marked instance says ready {used.IsReady} / {used.Value:R}, its twin {twin.IsReady} / {twin.Value:R}");
            }

            used.IsReady.ShouldBe(!double.IsNaN(used.Value), $"{kind}: readiness is NaN-ness at bar {i}");
        }

        pendingMarks.ShouldBeGreaterThan(50, "the run marks the instance pending often enough to mean something");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_pending_instance_is_not_ready_until_it_is_fed_whatever_it_held_before(string kind)
    {
        // two sessions of bars so that every kind has a value (the opening range of 15 minutes is ready at 09:29, the previous-session fields in the second)
        var bars = SeriesGenerator.Sessions(5, 2);
        var indicator = Make(kind);
        foreach (var bar in bars)
        {
            indicator.Update(bar);
        }

        indicator.IsReady.ShouldBeTrue($"{kind} has a value after two sessions");

        MarkPending(indicator);

        indicator.IsReady.ShouldBeFalse(kind);
        double.IsNaN(indicator.Value).ShouldBeTrue(kind);

        indicator.Reset();

        indicator.IsReady.ShouldBeFalse($"{kind} after Reset");
    }

    [Fact]
    public void Every_spec_the_frame_will_tell_about_a_new_date_is_created_as_an_instance_that_can_be_told()
    {
        var checkedSpecs = 0;
        foreach (var info in IndicatorCatalog.All)
        {
            foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
            {
                var spec = info.DefaultSpec(timeframe);
                if (!info.Supports(timeframe) || info.DateDriven || !info.RestartsEachSession(spec))
                {
                    continue;
                }

                var instance = info.Create(spec);

                (instance is IRestartingIndicator).ShouldBeTrue($"{info.Name} on {timeframe.ToCode()} restarts each session but its instance cannot be told");
                checkedSpecs++;
            }
        }

        // the twelve names on the six intraday timeframes
        checkedSpecs.ShouldBe(72);
    }
}
