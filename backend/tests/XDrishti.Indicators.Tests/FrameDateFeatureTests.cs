using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The indicator behind the date-driven features on its own (<see cref="DateFeature"/>, P3-01c): fed closed bars like any <see cref="IIndicator"/>, it takes a value at the first bar of each India date and
/// holds it for the date. The NIFTY 50 / INDIA VIX context kinds were dropped (ADR 0291).
/// </summary>
public sealed class FrameDateFeatureTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public void A_kind_that_is_not_one_of_()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new DateFeature((DateFeatureKind)99));
        Should.Throw<ArgumentOutOfRangeException>(() => new DateFeature((DateFeatureKind)(-1)));
        Enum.GetValues<DateFeatureKind>().Length.ShouldBe(9);
        foreach (var kind in Enum.GetValues<DateFeatureKind>())
        {
            new DateFeature(kind).Kind.ShouldBe(kind);
        }
    }

    [Fact]
    public void Without_its_data_every_kind_but_the_weekday_is_never_ready_and_the_weekday_is_ready_at_once()
    {
        var bar = Minutes.Close(Monday, Minutes.Open, 100);

        foreach (var kind in Enum.GetValues<DateFeatureKind>())
        {
            var feature = new DateFeature(kind);

            feature.IsReady.ShouldBeFalse();
            feature.Value.ShouldBeNaN();
            feature.Update(bar);
            feature.IsReady.ShouldBe(kind == DateFeatureKind.Weekday, kind.ToString());
        }
    }

    [Fact]
    public void A_second_bar_of_the_same_date_changes_nothing_and_the_first_bar_of_the_next_date_does()
    {
        var feature = new DateFeature(DateFeatureKind.Weekday);

        feature.Update(Minutes.Close(Monday, Minutes.Open, 100));
        feature.Update(Minutes.Close(Monday, Minutes.At(15, 29), 100));
        feature.Value.ShouldBe(1.0, "the same date holds its value");
        feature.Update(Minutes.Close(Monday.AddDays(1), Minutes.Open, 100));
        feature.Value.ShouldBe(2.0);
    }

    [Fact]
    public void Reset_forgets_the_date_and_the_value_and_the_same_date_is_read_again()
    {
        var feature = new DateFeature(DateFeatureKind.Weekday);
        var bar = Minutes.Close(Monday, Minutes.Open, 100);

        feature.Update(bar);
        var value = feature.Value;
        feature.Reset();

        feature.IsReady.ShouldBeFalse();
        feature.Value.ShouldBeNaN();
        feature.Update(bar);
        feature.Value.ShouldBe(value);
    }

    [Fact]
    public void A_date_fed_again_after_a_reset_is_read_again_even_when_it_is_the_epoch_day_a_zeroed_field_would_hold()
    {
        // India 1970-01-01 is day number 0, the value of a cleared "last date" field; it was a Thursday
        var bar = new Bar(0, 1, 1, 1, 1, 1);
        var feature = new DateFeature(DateFeatureKind.Weekday);

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
        var weekday = new DateFeature(DateFeatureKind.Weekday);
        var expiryDay = new DateFeature(DateFeatureKind.ExpiryDay, expiry);
        var withoutSet = new DateFeature(DateFeatureKind.ExpiryDay);

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
    public void The_warm_up_of_every_kind_is_one_date()
    {
        foreach (var kind in Enum.GetValues<DateFeatureKind>())
        {
            new DateFeature(kind).WarmUp.ShouldBe(1, kind.ToString());
        }
    }
}
