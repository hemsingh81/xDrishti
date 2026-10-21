using XDrishti.Api.Simulate.Gate;
using XDrishti.SimReader;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09d acceptance 4: what the machine checks of each spot line, and what it reports as not machine-checkable.</summary>
public sealed class SpotRulesTests
{
    private static SpotLine Line(string kind, string detail = "", DateOnly? date = null) =>
        new(7, "spot|" + kind + (detail.Length == 0 ? string.Empty : ":" + detail) + "|NSE:AAA|" + (date ?? Date).ToString("yyyy-MM-dd"), kind, detail, "NSE:AAA", date ?? Date);

    private static List<ReaderBar> Bars(int count, decimal price = 100m) =>
        [.. BuyDay().Take(count).Select(b => b with { Open = price, High = price, Low = price, Close = price })];

    [Fact]
    public void A_short_session_is_confirmed_when_the_window_holds_less_than_90_percent()
    {
        var r = SpotRules.Evaluate(Line("excluded_session", "short_session"), new SpotDay(Bars(300), null), false);

        r.Outcome.ShouldBe(SpotOutcome.Confirmed);
        r.Checked.ShouldContain("300 of 375");
    }

    [Fact]
    public void A_short_session_with_a_complete_day_of_bars_is_contradicted()
    {
        SpotRules.Evaluate(Line("excluded_session", "short_session"), new SpotDay(Bars(375), null), false).Outcome.ShouldBe(SpotOutcome.Contradicted);
        SpotRules.Evaluate(Line("excluded_session", "short_session"), new SpotDay(Bars(338), null), false).Outcome.ShouldBe(SpotOutcome.Contradicted);
        SpotRules.Evaluate(Line("excluded_session", "short_session"), new SpotDay(Bars(337), null), false).Outcome.ShouldBe(SpotOutcome.Confirmed);
    }

    [Fact]
    public void A_trade_of_the_pack_on_an_excluded_day_is_always_contradicted()
    {
        SpotRules.Evaluate(Line("excluded_session", "short_session"), new SpotDay(Bars(100), null), true).Outcome.ShouldBe(SpotOutcome.Contradicted);
        SpotRules.Evaluate(Line("excluded_session", "missing_session"), new SpotDay([], null), true).Outcome.ShouldBe(SpotOutcome.Contradicted);
    }

    [Fact]
    public void A_special_session_is_confirmed_only_for_a_weekend_and_otherwise_not_machine_checkable()
    {
        SpotRules.Evaluate(Line("excluded_session", "special_session", new DateOnly(2026, 3, 1)), new SpotDay(Bars(60), null), false).Outcome.ShouldBe(SpotOutcome.Confirmed);
        var weekday = SpotRules.Evaluate(Line("excluded_session", "special_session"), new SpotDay([], null), false);
        weekday.Outcome.ShouldBe(SpotOutcome.NotMachineCheckable);
        weekday.NotChecked.ShouldContain("calendar");
    }

    [Fact]
    public void A_missing_session_is_confirmed_with_no_bar_and_contradicted_when_bars_exist()
    {
        SpotRules.Evaluate(Line("excluded_session", "missing_session"), new SpotDay([], null), false).Outcome.ShouldBe(SpotOutcome.Confirmed);
        SpotRules.Evaluate(Line("excluded_session", "missing_session"), new SpotDay(Bars(10), null), false).Outcome.ShouldBe(SpotOutcome.Contradicted);
    }

    [Fact]
    public void A_flagged_day_is_confirmed_above_20_percent_and_otherwise_not_machine_checkable()
    {
        var previous = Bars(1, 100m)[0];
        SpotRules.Evaluate(Line("flagged_day"), new SpotDay(Bars(5, 70m), previous), false).Outcome.ShouldBe(SpotOutcome.Confirmed);
        SpotRules.Evaluate(Line("flagged_day"), new SpotDay(Bars(5, 80m), previous), false).Outcome.ShouldBe(SpotOutcome.NotMachineCheckable);
        var small = SpotRules.Evaluate(Line("flagged_day"), new SpotDay(Bars(5, 95m), previous), false);
        small.Outcome.ShouldBe(SpotOutcome.NotMachineCheckable);
        small.NotChecked.ShouldContain("adjusted");
        SpotRules.Evaluate(Line("flagged_day"), new SpotDay(Bars(5, 70m), null), false).Outcome.ShouldBe(SpotOutcome.NotMachineCheckable);
    }

    [Fact]
    public void Open_at_end_and_unknown_kinds_are_not_machine_checkable_never_a_silent_pass()
    {
        SpotRules.Evaluate(Line("open_at_end"), new SpotDay(Bars(375), null), false).Outcome.ShouldBe(SpotOutcome.NotMachineCheckable);
        SpotRules.Evaluate(Line("something_new"), new SpotDay(Bars(375), null), false).Outcome.ShouldBe(SpotOutcome.NotMachineCheckable);
    }
}
