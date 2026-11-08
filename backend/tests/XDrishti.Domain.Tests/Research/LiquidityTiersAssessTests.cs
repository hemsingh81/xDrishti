using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// CF-05 criterion 6 from day bars (ADR 0223): the tier from the 20 sessions before the entry date; the capacity against 0.5 % of that median (measured) and 1 % of the fill minute's traded value, which day bars cannot give: it reads unavailable and a pass is not reachable
/// from day bars alone. Hand tables with the exact equalities; a test-only fill-minute value shows the minute cap once a source exists.
/// </summary>
public sealed class LiquidityTiersAssessTests
{
    private const decimal Crore = LiquidityTiers.Crore;

    private const decimal Lakh = 100_000m;

    private const decimal ACut = 100 * Crore;

    private const decimal BCut = 20 * Crore;

    private static readonly DateOnly Entry = new(2026, 3, 2);

    /// <summary>A bar of close 100 whose traded value is exactly <paramref name="value"/> rupees.</summary>
    private static DailyBar Bar(DateOnly date, decimal value) => new(date, 100, 100, 100, 100, (long)(value / 100m));

    /// <summary><paramref name="sessions"/> bars before the entry date, each trading <paramref name="value"/>, then the entry day's bar trading <paramref name="entryDay"/> (none when null).</summary>
    private static List<DailyBar> Series(decimal value, decimal? entryDay, int sessions = 25)
    {
        var bars = Enumerable.Range(1, sessions).Reverse().Select(i => Bar(Entry.AddDays(-i), value)).ToList();
        if (entryDay is { } day)
        {
            bars.Add(Bar(Entry, day));
        }

        return bars;
    }

    private static (LiquidityTier Tier, CapacityCheck Capacity, bool? OverEntryDayShare) Assess(decimal? order, List<DailyBar> bars, decimal? fillMinuteValue = null) =>
        LiquidityTiers.Assess(order, bars, Entry, ACut, BCut, Capacity.ProposedMinuteShare, Capacity.ProposedDailyShare, fillMinuteValue);

    [Theory]
    [InlineData(450, LiquidityTier.A)]
    [InlineData(100, LiquidityTier.A)]
    [InlineData(99.9999, LiquidityTier.B)]
    [InlineData(60, LiquidityTier.B)]
    [InlineData(20, LiquidityTier.B)]
    [InlineData(19.9999, LiquidityTier.C)]
    [InlineData(12, LiquidityTier.C)]
    [InlineData(0, LiquidityTier.C)]
    public void The_median_of_the_sessions_before_the_entry_gives_the_tier_with_the_cuts_at_exact_equality(double crore, LiquidityTier expected)
    {
        Assess(1, Series((decimal)crore * Crore, 1000 * Crore)).Tier.ShouldBe(expected);
    }

    [Fact]
    public void The_entry_day_itself_does_not_enter_the_median_so_a_busy_entry_day_cannot_lift_the_tier()
    {
        Assess(1, Series(12 * Crore, 9_999 * Crore)).Tier.ShouldBe(LiquidityTier.C);
    }

    [Fact]
    public void Fewer_than_twenty_sessions_before_the_entry_is_no_tier_and_no_capacity_reading()
    {
        var (tier, capacity, _) = Assess(Lakh, Series(450 * Crore, 450 * Crore, sessions: 19));

        tier.ShouldBe(LiquidityTier.Unavailable);
        capacity.Breach.ShouldBeTrue();
        capacity.Reason.ShouldStartWith("unavailable");
    }

    [Theory]
    [InlineData(double.NaN, 1_000_000L)]
    [InlineData(100.0, -1L)]
    [InlineData(-100.0, 1_000_000L)]
    public void A_bar_without_a_usable_volume_or_close_among_the_last_twenty_is_no_tier(double close, long volume)
    {
        var bars = Series(450 * Crore, 450 * Crore);
        bars[^2] = new DailyBar(bars[^2].Date, close, close, close, close, volume);

        Assess(Lakh, bars).Tier.ShouldBe(LiquidityTier.Unavailable);
    }

    [Fact]
    public void A_zero_volume_session_is_a_traded_value_of_zero_not_a_missing_one()
    {
        var bars = Series(450 * Crore, 450 * Crore);
        for (var i = 0; i < 15; i++)
        {
            bars[^(2 + i)] = Bar(bars[^(2 + i)].Date, 0);
        }

        Assess(Lakh, bars).Tier.ShouldBe(LiquidityTier.C);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60 * 100_000)]
    public void From_day_bars_alone_no_order_is_within_capacity_the_fill_minute_cap_reads_unavailable_and_fails_closed(long order)
    {
        // even a one-rupee order in the most liquid name, and an order at exactly 0.5 % of the median, cannot pass without the fill minute
        var (_, capacity, _) = Assess(order, Series(120 * Crore, 10_000 * Crore));

        capacity.Breach.ShouldBeTrue();
        capacity.Reason.ShouldStartWith("unavailable");
        capacity.Reason.ShouldContain("the fill minute's traded value is not read");
        capacity.MinuteCap.ShouldBeNull();
        capacity.DailyCap.ShouldBe(60 * Lakh);
    }

    [Fact]
    public void A_breach_of_the_measured_median_cap_is_still_a_breach_when_the_fill_minute_is_not_read()
    {
        var at = Assess(60 * Lakh, Series(120 * Crore, 10_000 * Crore)).Capacity;
        var over = Assess((60 * Lakh) + 1, Series(120 * Crore, 10_000 * Crore)).Capacity;

        at.Reason.ShouldStartWith("unavailable", customMessage: "exactly at 0.5 % is within that cap, but the other cap is unread");
        over.Breach.ShouldBeTrue();
        over.Reason.ShouldContain("20-day median");
        over.Reason.ShouldNotStartWith("unavailable");
    }

    [Fact]
    public void With_a_fill_minute_value_a_source_could_give_the_caps_are_exact_at_the_equality_and_one_rupee_over_breaches()
    {
        var bars = Series(10_000 * Crore, null);

        var at = Assess(40_000m, bars, 40 * Lakh).Capacity;
        var over = Assess(40_001m, bars, 40 * Lakh).Capacity;

        at.MinuteCap.ShouldBe(40_000m);
        at.Breach.ShouldBeFalse();
        over.Breach.ShouldBeTrue();
        over.Reason.ShouldContain("fill minute");
    }

    [Fact]
    public void The_spec_example_of_one_lakh_in_a_minute_that_traded_two_crore_is_within_when_the_minute_is_given_and_the_median_is_one_twenty_crore()
    {
        var (tier, capacity, _) = Assess(1 * Lakh, Series(120 * Crore, null), 2 * Crore);

        tier.ShouldBe(LiquidityTier.A);
        capacity.Breach.ShouldBeFalse();
    }

    [Fact]
    public void The_entry_day_share_is_information_only_it_never_changes_the_capacity_reading()
    {
        var bars = Series(120 * Crore, 40 * Lakh);

        var within = Assess(40_000m, bars);
        var over = Assess(40_001m, bars);
        var noDay = Assess(40_001m, Series(120 * Crore, null));

        within.OverEntryDayShare.ShouldBe(false);
        over.OverEntryDayShare.ShouldBe(true);
        noDay.OverEntryDayShare.ShouldBeNull();
        within.Capacity.ShouldBe(Assess(40_000m, Series(120 * Crore, null)).Capacity);
        within.Capacity.Reason.ShouldStartWith("unavailable", customMessage: "an order inside the day share is not thereby within capacity");
    }

    [Fact]
    public void No_order_value_is_an_unavailable_reading_never_a_pass()
    {
        var noOrder = Assess(null, Series(120 * Crore, 2 * Crore), 2 * Crore);

        noOrder.Tier.ShouldBe(LiquidityTier.A);
        noOrder.Capacity.Breach.ShouldBeTrue();
        noOrder.Capacity.Reason.ShouldStartWith("unavailable");
        noOrder.OverEntryDayShare.ShouldBeNull();
    }

    [Fact]
    public void The_traded_value_is_close_times_volume_and_minus_one_when_unusable()
    {
        LiquidityTiers.TradedValue(new DailyBar(Entry, 1, 1, 1, 250.5, 1_000)).ShouldBe(250_500m);
        LiquidityTiers.TradedValue(new DailyBar(Entry, 1, 1, 1, double.PositiveInfinity, 1)).ShouldBe(-1m);
    }

    [Fact]
    public void The_traded_value_of_a_minute_is_close_times_volume_the_same_definition_as_a_day_bar()
    {
        LiquidityTiers.MinuteTradedValue(250.5, 1_000).ShouldBe(250_500m);
        LiquidityTiers.MinuteTradedValue(250.5, 1_000).ShouldBe(LiquidityTiers.TradedValue(new DailyBar(Entry, 1, 1, 1, 250.5, 1_000)));
    }

    [Theory]
    [InlineData(100.0, 0L)]
    [InlineData(100.0, -1L)]
    [InlineData(0.0, 1000L)]
    [InlineData(-5.0, 1000L)]
    [InlineData(double.NaN, 1000L)]
    [InlineData(double.PositiveInfinity, 1000L)]
    [InlineData(1e300, long.MaxValue)]
    public void A_minute_that_cannot_give_a_value_is_null_never_zero_so_the_cap_reads_unavailable(double close, long volume)
    {
        LiquidityTiers.MinuteTradedValue(close, volume).ShouldBeNull();
    }

    [Theory]
    [InlineData(40_000, false)] // exactly 1 % of the Rs 40 lakh minute
    [InlineData(40_001, true)] // one rupee over
    [InlineData(100_000, true)] // CF-05 AC6: Rs 1 lakh in a Rs 40 lakh minute is 2.5 %
    public void With_a_fill_minute_value_the_one_percent_cap_is_judged_and_a_pass_is_reachable(decimal order, bool breach)
    {
        var minute = LiquidityTiers.MinuteTradedValue(100, 40_000); // Rs 40 lakh

        var r = Assess(order, Series(450 * Crore, 1000 * Crore), minute);

        r.Tier.ShouldBe(LiquidityTier.A);
        r.Capacity.Breach.ShouldBe(breach);
        (r.Capacity.Reason.Length > 0 && !r.Capacity.Reason.StartsWith("unavailable", StringComparison.Ordinal)).ShouldBe(breach);
    }

    [Fact]
    public void A_minute_of_Rs_2_crore_holds_an_order_of_Rs_1_lakh_at_half_a_percent()
    {
        var r = Assess(100_000m, Series(450 * Crore, 1000 * Crore), LiquidityTiers.MinuteTradedValue(100, 200_000));

        r.Capacity.Breach.ShouldBeFalse();
        r.Capacity.Reason.ShouldBeEmpty();
    }
}
