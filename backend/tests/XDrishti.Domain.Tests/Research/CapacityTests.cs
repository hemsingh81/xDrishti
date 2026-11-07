using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>CF-05 criterion 6 (capacity): at most 1 % of the fill minute's traded value and at most 0.5 % of the 20-day median daily value.</summary>
public sealed class CapacityTests
{
    private const decimal Lakh = 100_000m;

    private const decimal Crore = 10_000_000m;

    [Fact]
    public void An_order_of_one_lakh_in_a_minute_that_traded_forty_lakh_is_two_and_a_half_percent_and_a_breach()
    {
        var check = Capacity.Check(1 * Lakh, 40 * Lakh, 120 * Crore, Capacity.ProposedMinuteShare, Capacity.ProposedDailyShare);

        check.MinuteCap.ShouldBe(40_000m);
        check.Breach.ShouldBeTrue();
        check.Reason.ShouldContain("fill minute");
    }

    [Fact]
    public void The_same_order_in_a_minute_that_traded_two_crore_is_half_a_percent_and_ok()
    {
        var check = Capacity.Check(1 * Lakh, 2 * Crore, 120 * Crore, Capacity.ProposedMinuteShare, Capacity.ProposedDailyShare);

        check.Breach.ShouldBeFalse();
        check.Reason.ShouldBeEmpty();
    }

    [Fact]
    public void A_twenty_day_median_of_one_twenty_crore_caps_the_order_at_sixty_lakh()
    {
        var within = Capacity.Check(60 * Lakh, 10_000 * Crore, 120 * Crore, Capacity.ProposedMinuteShare, Capacity.ProposedDailyShare);
        var over = Capacity.Check((60 * Lakh) + 1, 10_000 * Crore, 120 * Crore, Capacity.ProposedMinuteShare, Capacity.ProposedDailyShare);

        within.DailyCap.ShouldBe(60 * Lakh);
        within.Breach.ShouldBeFalse();
        over.Breach.ShouldBeTrue();
        over.Reason.ShouldContain("20-day median");
    }

    [Fact]
    public void An_order_over_a_known_cap_is_a_breach_even_when_the_other_cap_cannot_be_read_and_the_unread_cap_is_named()
    {
        var overMedian = Capacity.Check(60 * Lakh + 1, null, 120 * Crore, 0.01m, 0.005m);
        var overMinute = Capacity.Check(Lakh, 4 * Lakh, null, 0.01m, 0.005m);
        var inside = Capacity.Check(Lakh, null, 120 * Crore, 0.01m, 0.005m);

        overMedian.Reason.ShouldBe("over the share of the 20-day median traded value");
        overMinute.Reason.ShouldBe("over the share of the fill minute's traded value");
        inside.Reason.ShouldBe("unavailable: the fill minute's traded value is not read");
        Capacity.Check(Lakh, null, null, 0.01m, 0.005m).Reason.ShouldBe("unavailable: the fill minute's traded value is not read; the 20-day median traded value is not known");
    }

    [Fact]
    public void A_missing_traded_value_is_a_breach_with_an_unavailable_reason_never_a_pass()
    {
        Capacity.Check(Lakh, null, 120 * Crore, 0.01m, 0.005m).Breach.ShouldBeTrue();
        var noMedian = Capacity.Check(Lakh, 2 * Crore, null, 0.01m, 0.005m);

        noMedian.Breach.ShouldBeTrue();
        noMedian.Reason.ShouldStartWith("unavailable");
    }
}
