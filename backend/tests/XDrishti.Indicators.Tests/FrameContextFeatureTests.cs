using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The indicator behind the date-driven features on its own (<see cref="ContextFeature"/>, P3-01c): fed closed bars like any <see cref="IIndicator"/>, it takes a value at the first bar of each India date and
/// holds it for the date; its warm-up is counted in days of the context, exactly for the options of the context it was given.
/// </summary>
public sealed class FrameContextFeatureTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public void A_kind_that_is_not_one_of_the_twelve_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ContextFeature((ContextFeatureKind)99));
        Should.Throw<ArgumentOutOfRangeException>(() => new ContextFeature((ContextFeatureKind)(-1)));
        Enum.GetValues<ContextFeatureKind>().Length.ShouldBe(12);
        foreach (var kind in Enum.GetValues<ContextFeatureKind>())
        {
            new ContextFeature(kind).Kind.ShouldBe(kind);
        }
    }

    [Fact]
    public void Each_kind_reads_the_field_of_the_context_that_carries_its_name()
    {
        var days = FrameContextFixtures.Consecutive(30, 60, Monday);
        var context = FrameContextFixtures.Build(days, FrameContextFixtures.Small);
        var date = days[45].Date;
        context.TryAsOf(date, out var values).ShouldBeTrue();
        var expected = new Dictionary<ContextFeatureKind, double>
        {
            [ContextFeatureKind.IndexTrend] = values.IndexTrend,
            [ContextFeatureKind.IndexAdx] = values.IndexAdx,
            [ContextFeatureKind.IndexVolPct] = values.IndexVolPct,
            [ContextFeatureKind.Vix] = values.Vix,
            [ContextFeatureKind.VixPct] = values.VixPct,
            [ContextFeatureKind.VixChg] = values.VixChg,
            [ContextFeatureKind.Breadth] = values.Breadth,
            [ContextFeatureKind.RegimeTrend] = values.RegimeTrend,
            [ContextFeatureKind.RegimeVol] = values.RegimeVol,
            [ContextFeatureKind.Regime] = values.Regime,
        };

        values.Date.ShouldBe(days[44].Date);
        foreach (var (kind, value) in expected)
        {
            var feature = new ContextFeature(kind, context);

            feature.Update(Minutes.Close(date, Minutes.Open, 100));

            Tolerance.Identical(value, feature.Value).ShouldBeTrue(kind.ToString());
            feature.IsReady.ShouldBe(!double.IsNaN(value), kind.ToString());
        }

        expected.Values.Count(v => !double.IsNaN(v)).ShouldBe(10, "all ten are numbers on day 45 with the small options and a VIX and a breadth on every day");
    }

    [Fact]
    public void Without_a_context_the_context_kinds_are_never_ready_and_the_weekday_is_ready_at_once()
    {
        var bar = Minutes.Close(Monday, Minutes.Open, 100);

        foreach (var kind in Enum.GetValues<ContextFeatureKind>())
        {
            var feature = new ContextFeature(kind);

            feature.IsReady.ShouldBeFalse();
            feature.Value.ShouldBeNaN();
            feature.Update(bar);
            feature.IsReady.ShouldBe(kind == ContextFeatureKind.Weekday, kind.ToString());
        }
    }

    [Fact]
    public void A_second_bar_of_the_same_date_changes_nothing_and_the_first_bar_of_the_next_date_does()
    {
        var days = FrameContextFixtures.Consecutive(31, 40, Monday);
        var context = FrameContextFixtures.Build(days.Take(30), FrameContextFixtures.Small);
        var feature = new ContextFeature(ContextFeatureKind.Vix, context);
        var date = days[30].Date;

        feature.Update(Minutes.Close(date, Minutes.Open, 100));
        var first = feature.Value;
        context.Append(days[30]).IsSuccess.ShouldBeTrue();
        feature.Update(Minutes.Close(date, Minutes.At(15, 29), 100));

        first.ShouldBe(days[29].VixClose!.Value);
        feature.Value.ShouldBe(first, "the same date holds its value, though the context grew in between");
        feature.Update(Minutes.Close(days[31].Date, Minutes.Open, 100));
        feature.Value.ShouldBe(days[30].VixClose!.Value);
    }

    [Fact]
    public void Reset_forgets_the_date_and_the_value_and_the_same_date_is_read_again()
    {
        var days = FrameContextFixtures.Consecutive(32, 40, Monday);
        var feature = new ContextFeature(ContextFeatureKind.Breadth, FrameContextFixtures.Build(days, FrameContextFixtures.Small));
        var bar = Minutes.Close(days[20].Date, Minutes.Open, 100);

        feature.Update(bar);
        var value = feature.Value;
        feature.Reset();

        feature.IsReady.ShouldBeFalse();
        feature.Value.ShouldBeNaN();
        feature.Update(bar);
        feature.Value.ShouldBe(value);
        value.ShouldBe(days[19].Breadth!.Value);
    }

    [Fact]
    public void A_date_fed_again_after_a_reset_is_read_again_even_when_it_is_the_epoch_day_a_zeroed_field_would_hold()
    {
        // India 1970-01-01 is day number 0, the value of a cleared "last date" field; it was a Thursday
        var bar = new Bar(0, 1, 1, 1, 1, 1);
        var feature = new ContextFeature(ContextFeatureKind.Weekday);

        feature.Update(bar);
        feature.Value.ShouldBe(4.0);
        feature.Reset();
        feature.IsReady.ShouldBeFalse();
        feature.Update(bar);

        feature.Value.ShouldBe(4.0, "the first bar after a reset is the first bar of its date, whatever the date is");
    }

    [Fact]
    public void The_weekday_and_the_expiry_day_read_the_date_of_the_bar()
    {
        var expiry = new HashSet<DateOnly> { Monday.AddDays(3) };
        var weekday = new ContextFeature(ContextFeatureKind.Weekday);
        var expiryDay = new ContextFeature(ContextFeatureKind.ExpiryDay, null, expiry);
        var withoutSet = new ContextFeature(ContextFeatureKind.ExpiryDay);

        for (var i = 0; i < 7; i++)
        {
            var bar = Minutes.Close(Monday.AddDays(i), Minutes.Open, 100);
            weekday.Update(bar);
            expiryDay.Update(bar);
            withoutSet.Update(bar);

            weekday.Value.ShouldBe(i + 1);
            expiryDay.Value.ShouldBe(i == 3 ? 1.0 : 0.0);
            withoutSet.IsReady.ShouldBeFalse("no set was given");
        }
    }

    [Fact]
    public void The_warm_up_is_in_days_of_the_context_and_exact_for_the_options_the_instance_was_given()
    {
        int Warm(ContextFeatureKind kind, ContextOptions? options = null) =>
            new ContextFeature(kind, options is null ? null : FrameContextFixtures.Build([], options)).WarmUp;

        // the defaults, with or without a context built with them
        Warm(ContextFeatureKind.Weekday).ShouldBe(1);
        Warm(ContextFeatureKind.ExpiryDay).ShouldBe(1);
        Warm(ContextFeatureKind.Vix).ShouldBe(1);
        Warm(ContextFeatureKind.Breadth).ShouldBe(1);
        Warm(ContextFeatureKind.VixChg).ShouldBe(2);
        Warm(ContextFeatureKind.IndexAdx).ShouldBe(28);
        Warm(ContextFeatureKind.IndexTrend).ShouldBe(55);
        Warm(ContextFeatureKind.RegimeTrend, new ContextOptions()).ShouldBe(55);
        Warm(ContextFeatureKind.VixPct).ShouldBe(252);
        Warm(ContextFeatureKind.IndexVolPct).ShouldBe(265);
        Warm(ContextFeatureKind.RegimeVol, new ContextOptions()).ShouldBe(265);
        Warm(ContextFeatureKind.Regime).ShouldBe(265);

        // other options: small ones, and a trend that takes longer than the volatility (the regime waits for it)
        Warm(ContextFeatureKind.IndexTrend, FrameContextFixtures.Small).ShouldBe(8);
        Warm(ContextFeatureKind.IndexAdx, FrameContextFixtures.Small).ShouldBe(6);
        Warm(ContextFeatureKind.VixPct, FrameContextFixtures.Small).ShouldBe(10);
        Warm(ContextFeatureKind.Regime, FrameContextFixtures.Small).ShouldBe(12);
        Warm(ContextFeatureKind.IndexTrend, FrameContextFixtures.SlowTrend).ShouldBe(34);
        Warm(ContextFeatureKind.RegimeVol, FrameContextFixtures.SlowTrend).ShouldBe(6);
        Warm(ContextFeatureKind.Regime, FrameContextFixtures.SlowTrend).ShouldBe(34);
        Warm(ContextFeatureKind.IndexTrend, new ContextOptions(TrendSmaPeriod: 5, TrendSlopeDays: 1, AdxPeriod: 20)).ShouldBe(40, "the ADX takes longer than the average and its slope");
    }
}
