using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>Two runs over the same input give bit-identical outputs (criterion 10); <c>Reset()</c> returns every indicator to its state after construction (criterion 2).</summary>
public sealed class DeterminismAndResetTests
{
    private static List<long> Bits(List<Bar> bars, List<FeatureSpec> specs)
    {
        var frame = FeatureFrame.Create(specs).Value;
        var bits = new List<long>(bars.Count * specs.Count);
        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var h = 0; h < specs.Count; h++)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(frame[h]));
            }
        }

        return bits;
    }

    [Fact]
    public void Two_runs_over_the_same_bars_give_the_same_bits_also_on_another_thread()
    {
        var bars = SeriesGenerator.Random(5, specialSessions: true);
        var specs = IndicatorCatalog.SupportedTimeframes.SelectMany(TestSpecs.Variants).ToList();

        var first = Bits(bars, specs);
        var second = Bits(bars, specs);
        List<long>? third = null;
        var thread = new Thread(() => third = Bits(bars, specs));
        thread.Start();
        thread.Join();

        second.ShouldBe(first);
        third.ShouldNotBeNull().ShouldBe(first);
    }

    [Fact]
    public void Two_instances_fed_the_same_bars_agree_to_the_last_bit()
    {
        var bars = SeriesGenerator.Random(11);

        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var a = info.Create(spec);
            var b = info.Create(spec);
            foreach (var bar in bars)
            {
                a.Update(bar);
                b.Update(bar);
                Tolerance.Identical(a.Value, b.Value).ShouldBeTrue(TestSpecs.Describe(spec));
            }
        }
    }

    [Fact]
    public void Reset_returns_every_indicator_to_the_state_after_construction()
    {
        var before = SeriesGenerator.Random(21, 800, 1_500, specialSessions: true);
        var after = SeriesGenerator.Random(22, 800, 1_500);

        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var used = info.Create(spec);
            foreach (var bar in before)
            {
                used.Update(bar);
            }

            used.Reset();
            var fresh = info.Create(spec);

            used.IsReady.ShouldBe(fresh.IsReady, TestSpecs.Describe(spec));
            Tolerance.Identical(used.Value, fresh.Value).ShouldBeTrue(TestSpecs.Describe(spec));
            foreach (var bar in after)
            {
                used.Update(bar);
                fresh.Update(bar);
                if (!Tolerance.Identical(used.Value, fresh.Value) || used.IsReady != fresh.IsReady)
                {
                    Assert.Fail($"{TestSpecs.Describe(spec)} differs after Reset at {bar.Start:u}");
                }
            }
        }
    }

    [Fact]
    public void A_frame_that_was_reset_equals_a_new_one()
    {
        var specs = IndicatorCatalog.SupportedTimeframes.SelectMany(TestSpecs.Variants).ToList();
        var bars = SeriesGenerator.Random(31, 600, 900, specialSessions: true);
        var used = FeatureFrame.Create(specs).Value;
        foreach (var bar in SeriesGenerator.Random(32, 600, 900))
        {
            used.Step(bar);
        }

        used.Reset();
        var fresh = FeatureFrame.Create(specs).Value;
        foreach (var bar in bars)
        {
            used.Step(bar);
            fresh.Step(bar);
            for (var h = 0; h < specs.Count; h++)
            {
                Tolerance.Identical(used[h], fresh[h]).ShouldBeTrue($"{TestSpecs.Describe(specs[h])} at {bar.Start:u}");
            }
        }
    }

    [Fact]
    public void Indicators_do_not_depend_on_the_current_culture()
    {
        var bars = SeriesGenerator.Random(41, 600, 800);
        var specs = TestSpecs.Variants(Timeframe.M1);
        var expected = Bits(bars, specs);
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Bits(bars, specs).ShouldBe(expected);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
