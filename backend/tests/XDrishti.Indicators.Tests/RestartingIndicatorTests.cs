using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// <see cref="IRestartingIndicator.MarkPending"/> on each of the classes that restart each session (<see cref="Vwap"/>, <see cref="OrHigh"/>, <see cref="OrLow"/> and the nine fields of <see cref="SessionContext"/>):
/// after it the indicator reports not ready and NaN, the next bar clears it and the value is the new date's, a reset leaves a fresh instance, and readiness stays "the value is not NaN" (ADR 0031, Amendment 8) in every
/// state. The numbers are worked out by hand from the bars below.
/// <code>
/// Monday    09:15 open 100 high 105 low 98  close 102 volume 10     Tuesday   09:15 open 120 high 125 low 118 close 121 volume 20     Wednesday 09:15 open 130 high 133 low 129 close 131 volume 25
///           09:16 open 102 high 110 low 101 close 108 volume 30               09:16 open 121 high 124 low 119 close 122 volume 20
/// typical price (high + low + close) / 3:   Monday 305/3 and 319/3;  Tuesday 364/3 and 365/3
/// </code>
/// </summary>
public sealed class RestartingIndicatorTests
{
    private static readonly Bar Mon1 = Minutes.Bar(BarFixtures.Mon, Minutes.Open, 100, 105, 98, 102, 10);

    private static readonly Bar Mon2 = Minutes.Bar(BarFixtures.Mon, Minutes.Open + 1, 102, 110, 101, 108, 30);

    private static readonly Bar Tue1 = Minutes.Bar(BarFixtures.Tue, Minutes.Open, 120, 125, 118, 121, 20);

    private static readonly Bar Tue2 = Minutes.Bar(BarFixtures.Tue, Minutes.Open + 1, 121, 124, 119, 122, 20);

    private static readonly Bar Wed1 = Minutes.Bar(BarFixtures.Wed, Minutes.Open, 130, 133, 129, 131, 25);

    private static void MarkPending(IIndicator indicator) => ((IRestartingIndicator)indicator).MarkPending();

    private static void ShouldBeNotReady(IIndicator indicator, string label)
    {
        indicator.IsReady.ShouldBeFalse(label);
        indicator.Value.ShouldBeNaN(label);
    }

    private static void ShouldBeReadyWith(IIndicator indicator, double expected, string label)
    {
        indicator.IsReady.ShouldBeTrue(label);
        indicator.Value.ShouldBe(expected, 1e-9, label);
    }

    [Fact]
    public void Vwap_is_not_ready_after_it_is_marked_pending_and_the_next_bar_of_a_new_date_gives_the_new_dates_value()
    {
        var vwap = new Vwap();
        vwap.Update(Mon1);
        vwap.Update(Mon2);
        ShouldBeReadyWith(vwap, 12_620.0 / 120, "Monday: (305/3 · 10 + 319/3 · 30) / 40");

        MarkPending(vwap);

        ShouldBeNotReady(vwap, "pending");
        vwap.Update(Tue1);
        ShouldBeReadyWith(vwap, 364.0 / 3, "Tuesday's first bar: its typical price (125 + 118 + 121) / 3");
        vwap.Update(Tue2);
        ShouldBeReadyWith(vwap, (364.0 / 3 + 365.0 / 3) / 2, "Tuesday's two bars of equal volume: the mean of the two typical prices");
    }

    [Fact]
    public void A_pending_vwap_that_is_fed_a_bar_of_the_same_date_goes_on_with_that_session()
    {
        // the frame never does this, but the instance must not lose its state: the next Update clears the flag and the date logic decides
        var vwap = new Vwap();
        vwap.Update(Mon1);

        MarkPending(vwap);
        ShouldBeNotReady(vwap, "pending");
        vwap.Update(Mon2);

        ShouldBeReadyWith(vwap, 12_620.0 / 120, "Monday goes on");
    }

    [Fact]
    public void Marking_a_vwap_that_has_seen_no_bar_changes_nothing_the_first_bar_makes_it_ready()
    {
        var vwap = new Vwap();

        MarkPending(vwap);
        ShouldBeNotReady(vwap, "fresh and pending");
        vwap.Update(Mon1);

        ShouldBeReadyWith(vwap, 305.0 / 3, "(105 + 98 + 102) / 3");
    }

    [Fact]
    public void An_opening_range_of_one_bar_is_not_ready_when_pending_and_is_ready_on_the_first_bar_of_the_new_date()
    {
        var orHigh = new OrHigh(1);
        var orLow = new OrLow(1);
        orHigh.Update(Mon1);
        orLow.Update(Mon1);
        ShouldBeReadyWith(orHigh, 105, "Monday's first bar high");
        ShouldBeReadyWith(orLow, 98, "Monday's first bar low");

        MarkPending(orHigh);
        MarkPending(orLow);

        ShouldBeNotReady(orHigh, "or_high pending");
        ShouldBeNotReady(orLow, "or_low pending");
        orHigh.Update(Tue1);
        orLow.Update(Tue1);
        ShouldBeReadyWith(orHigh, 125, "Tuesday's first bar");
        ShouldBeReadyWith(orLow, 118, "Tuesday's first bar");
    }

    [Fact]
    public void An_opening_range_of_two_bars_is_pending_then_not_ready_until_its_second_bar_of_the_new_date_has_come()
    {
        var orHigh = new OrHigh(2);
        var orLow = new OrLow(2);
        foreach (var bar in new[] { Mon1, Mon2 })
        {
            orHigh.Update(bar);
            orLow.Update(bar);
        }

        ShouldBeReadyWith(orHigh, 110, "Monday: max(105, 110)");
        ShouldBeReadyWith(orLow, 98, "Monday: min(98, 101)");

        MarkPending(orHigh);
        MarkPending(orLow);
        ShouldBeNotReady(orHigh, "pending");
        ShouldBeNotReady(orLow, "pending");

        orHigh.Update(Tue1);
        orLow.Update(Tue1);
        ShouldBeNotReady(orHigh, "Tuesday's range has one of its two bars");
        ShouldBeNotReady(orLow, "Tuesday's range has one of its two bars");

        orHigh.Update(Tue2);
        orLow.Update(Tue2);
        ShouldBeReadyWith(orHigh, 125, "max(125, 124)");
        ShouldBeReadyWith(orLow, 118, "min(118, 119)");
    }

    public static TheoryData<SessionField, double, double> Fields => new()
    {
        // field, its value after Tuesday's two bars, its value after Wednesday's first bar. Previous session: Monday (open 100, high 110, low 98, close 108, volume 40) for Tuesday and Tuesday (open 120, high 125,
        // low 118, close 122, volume 40) for Wednesday; today: Tuesday's open 120, high max(125, 124), low min(118, 119) and Wednesday's first bar; gap = (open of the day - previous close) / previous close · 100
        { SessionField.PrevOpen, 100, 120 },
        { SessionField.PrevHigh, 110, 125 },
        { SessionField.PrevLow, 98, 118 },
        { SessionField.PrevClose, 108, 122 },
        { SessionField.PrevVolume, 40, 40 },
        { SessionField.TodayOpen, 120, 130 },
        { SessionField.TodayHigh, 125, 133 },
        { SessionField.TodayLow, 118, 129 },
        { SessionField.GapPct, (120.0 - 108) / 108 * 100, (130.0 - 122) / 122 * 100 },
    };

    [Theory]
    [MemberData(nameof(Fields))]
    public void Every_session_field_is_not_ready_after_it_is_marked_pending_and_the_next_bar_gives_the_new_dates_value(SessionField field, double afterTuesday, double afterWednesday)
    {
        var context = new SessionContext(field);
        foreach (var bar in new[] { Mon1, Mon2, Tue1, Tue2 })
        {
            context.Update(bar);
        }

        ShouldBeReadyWith(context, afterTuesday, field + " on Tuesday");

        MarkPending(context);

        ShouldBeNotReady(context, field + " pending");
        context.Update(Wed1);
        ShouldBeReadyWith(context, afterWednesday, field + " on Wednesday's first bar");
    }

    public static TheoryData<string> Names { get; } = ["vwap", "or_high(1)", "or_low(1)", "or_high(2)", .. Enum.GetValues<SessionField>().Select(f => "context " + f)];

    private static IIndicator Make(string name) => name switch
    {
        "vwap" => new Vwap(),
        "or_high(1)" => new OrHigh(1),
        "or_low(1)" => new OrLow(1),
        "or_high(2)" => new OrHigh(2),
        _ => new SessionContext(Enum.Parse<SessionField>(name["context ".Length..])),
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void A_reset_after_a_pending_mark_leaves_an_instance_that_behaves_like_a_new_one(string name)
    {
        var used = Make(name);
        var fresh = Make(name);
        used.Update(Mon1);
        used.Update(Tue1);
        MarkPending(used);

        used.Reset();

        ShouldBeNotReady(used, name + " after the reset");
        foreach (var bar in new[] { Mon1, Mon2, Tue1, Tue2, Wed1 })
        {
            used.Update(bar);
            fresh.Update(bar);
            Tolerance.Identical(used.Value, fresh.Value).ShouldBeTrue($"{name}: {used.Value:R} against a fresh instance's {fresh.Value:R}");
            used.IsReady.ShouldBe(fresh.IsReady, name);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Readiness_is_the_value_not_being_nan_before_during_and_after_the_pending_state(string name)
    {
        var indicator = Make(name);
        void Check(string state) => indicator.IsReady.ShouldBe(!double.IsNaN(indicator.Value), $"{name}, {state}");

        Check("new");
        foreach (var bar in new[] { Mon1, Mon2, Tue1, Tue2 })
        {
            indicator.Update(bar);
            Check("after " + bar.EpochSeconds);
        }

        MarkPending(indicator);
        Check("pending");
        indicator.Update(Wed1);
        Check("after the pending state");
    }
}
