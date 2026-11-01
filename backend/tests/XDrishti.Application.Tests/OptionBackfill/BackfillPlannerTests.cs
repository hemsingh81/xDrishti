using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;
using static XDrishti.Application.Tests.OptionBackfill.BackfillSamples;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>The fetch keys of the backfill (P14-03 criterion 2): exact counts, a fixed order, the BANKNIFTY weekly cut, the windows at month and year ends.</summary>
public sealed class BackfillPlannerTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static List<BackfillItem> Plan(DateOnly? today = null, OptionBackfillSettings? settings = null, params BackfillTarget[] targets) =>
        [.. BackfillPlanner.Plan(targets.Length == 0 ? [Nifty, BankNifty, Stock] : targets, settings ?? OptionBackfillSettings.Off, today ?? Today)];

    [Fact]
    public void Five_years_are_61_windows_of_30_days_on_a_fixed_grid_the_newest_ending_by_the_first_of_the_month()
    {
        var windows = BackfillPlanner.Windows(Today, 5);

        windows.Count.ShouldBe(61, "on this day; 60 to 62 on others");
        windows[0].AddDays(BackfillKey.WindowDays).ShouldBeLessThanOrEqualTo(new DateOnly(2026, 11, 1));
        windows[0].AddDays(2 * BackfillKey.WindowDays).ShouldBeGreaterThan(new DateOnly(2026, 11, 1), "the newest window is the last that ends by the first of the month");
        windows.Zip(windows.Skip(1)).ShouldAllBe(p => p.First.DayNumber - p.Second.DayNumber == 30, "the windows tile without a gap");
        windows.ShouldAllBe(w => (w.DayNumber - BackfillPlanner.Epoch.DayNumber) % BackfillKey.WindowDays == 0, "every window is on the grid of the epoch");
    }

    [Fact]
    public void A_new_month_only_adds_windows_at_the_new_end_so_a_done_key_stays_done()
    {
        for (var month = 1; month < 12; month++)
        {
            var earlier = BackfillPlanner.Windows(new DateOnly(2026, month, 17), 5);
            var later = BackfillPlanner.Windows(new DateOnly(2026, month + 1, 3), 5);

            later.Count.ShouldBeInRange(earlier.Count - 1, earlier.Count + 1);
            var kept = earlier.Where(later.Contains).ToList();
            kept.Count.ShouldBeGreaterThanOrEqualTo(earlier.Count - 3, "at most the oldest few fall off");
            kept.ShouldBe(earlier.Take(kept.Count), "what stays is the newer end of the earlier plan, window for window");
            later.Take(later.Count - kept.Count).ShouldAllBe(w => w > earlier[0], "the new windows are all newer than the old plan");
        }
    }

    [Fact]
    public void The_same_month_is_the_same_plan_on_any_day_and_the_oldest_request_is_clamped_to_five_years()
    {
        BackfillPlanner.Windows(new DateOnly(2026, 11, 1), 5).ShouldBe(BackfillPlanner.Windows(new DateOnly(2026, 11, 30), 5));
        var oldest = Plan(Today, Small with { Years = 5 }, Nifty).Last();

        oldest.Key.From.ShouldBeLessThan(new DateOnly(2021, 11, 1).AddDays(30));
        oldest.ToRequest().From.ShouldBeGreaterThanOrEqualTo(new DateOnly(2021, 11, 1));
        oldest.ToRequest().To.ShouldBe(oldest.Key.To);
        Plan(Today, Small with { Years = 5 }, Nifty).Where(i => i.Key.From >= new DateOnly(2021, 11, 1)).ShouldAllBe(i => i.ToRequest().From == i.Key.From);
    }

    [Theory]
    [InlineData(2026, 1, 1, 12, 13)]
    [InlineData(2026, 12, 1, 12, 13)]
    [InlineData(2025, 1, 5, 60, 61)]
    public void The_window_count_follows_the_years_at_a_year_end_too(int year, int month, int years, int low, int high)
    {
        BackfillPlanner.Windows(new DateOnly(year, month, 15), years).Count.ShouldBeInRange(low, high);
    }

    [Fact]
    public void With_the_defaults_the_keys_are_exactly_those_of_the_spec()
    {
        var plan = Plan();

        var nifty = plan.Where(i => i.Key.UnderlyingId == NiftyId).ToList();
        var bank = plan.Where(i => i.Key.UnderlyingId == BankNiftyId).ToList();
        var stock = plan.Where(i => i.Key.UnderlyingId == StockId).ToList();
        var cut = BackfillPlanner.Windows(Today, 5).Count(w => w.AddDays(30) <= new DateOnly(2024, 11, 20));
        nifty.Count.ShouldBe(3 * 21 * 2 * 61, "WEEK 1, WEEK 2, MONTH 1 at ATM-10..ATM+10, both sides, 61 windows");
        bank.Count.ShouldBe((2 * 21 * 2 * 61) + (21 * 2 * cut), "MONTH 1 and 2 in full and WEEK 1 only for windows that end on or before 2024-11-20");
        cut.ShouldBe(37, "the BANKNIFTY weekly windows that end by 2024-11-20, on 2026-11-01");
        plan.Count.ShouldBe(nifty.Count + bank.Count + stock.Count);
        stock.Count.ShouldBe(20 > 1 ? 1 * 7 * 2 * 61 : 0, "one share here; 20 shares make the spec's 17,080");
        plan.Count.ShouldBe(nifty.Count + bank.Count + stock.Count);
        nifty.Select(i => (i.Key.ExpiryFlag, i.Key.ExpiryCode)).Distinct().ShouldBe([('W', 1), ('W', 2), ('M', 1)]);
        bank.Select(i => (i.Key.ExpiryFlag, i.Key.ExpiryCode)).Distinct().ShouldBe([('M', 1), ('M', 2), ('W', 1)]);
        nifty.ShouldAllBe(i => i.Key.IntervalMinutes == 5 && i.Instrument == "OPTIDX");
        stock.ShouldAllBe(i => i.Key.IntervalMinutes == 15 && i.Instrument == "OPTSTK" && i.Key.ExpiryFlag == 'M' && i.Key.ExpiryCode == 1 && Math.Abs(i.Key.Offset) <= 3);
    }

    [Fact]
    public void Twenty_shares_make_17080_stock_keys_and_the_spec_totals_hold()
    {
        var shares = Enumerable.Range(0, 20).Select(i => new BackfillTarget(Guid.NewGuid(), "NSE:S" + i, 1000 + i, false, 50)).ToArray();

        var plan = Plan(Today, null, [Nifty, BankNifty, .. shares]);

        plan.Count(i => i.Instrument == "OPTSTK").ShouldBe(17_080);
        plan.Count.ShouldBe(31_444, "14,364 index keys and 17,080 stock keys: the requests of the ADR");
        var cut = BackfillPlanner.Windows(Today, 5).Count(w => w.AddDays(30) <= new DateOnly(2024, 11, 20));
        plan.Count(i => i.Instrument == "OPTIDX").ShouldBe(7_686 + 5_124 + (21 * 2 * cut));
    }

    [Fact]
    public void The_order_is_underlying_then_series_then_offset_then_side_then_the_newest_window_first()
    {
        var plan = Plan(Today, Small with { IndexOffsets = 1 }, Nifty);

        plan.Take(24).Select(i => (i.Key.ExpiryFlag, i.Key.ExpiryCode, i.Key.Offset, i.Key.Side)).Distinct().ShouldBe([('W', 1, -1, "CE"), ('W', 1, -1, "PE")]);
        plan[0].Key.From.ShouldBeGreaterThan(plan[1].Key.From);
        plan.Select(i => (i.Key.ExpiryFlag, i.Key.ExpiryCode)).Distinct().ShouldBe([('W', 1), ('W', 2), ('M', 1)]);
        plan.Select(i => i.Key.Offset).Distinct().ShouldBe([-1, 0, 1]);
        plan.Count.ShouldBe(3 * 3 * 2 * 12);
    }

    [Fact]
    public void The_banknifty_weekly_cut_is_at_2024_11_20_and_a_window_ending_on_it_is_kept()
    {
        var plan = Plan(new DateOnly(2025, 3, 1), Small with { Years = 3, IndexOffsets = 0 }, BankNifty);

        var weekly = plan.Where(i => i.Key.ExpiryFlag == 'W').Select(i => i.Key).ToList();
        weekly.ShouldNotBeEmpty();
        weekly.ShouldAllBe(k => k.To <= new DateOnly(2024, 11, 20));
        plan.Where(i => i.Key.ExpiryFlag == 'M').Any(i => i.Key.To > new DateOnly(2024, 11, 20)).ShouldBeTrue("the monthly series is not cut");
    }

    [Fact]
    public void Only_nifty_and_banknifty_among_the_indices_and_the_first_stock_count_shares_are_planned()
    {
        var finnifty = new BackfillTarget(Guid.NewGuid(), "NSE:FINNIFTY", 27, true, 5_000);
        var shares = Enumerable.Range(0, 5).Select(i => new BackfillTarget(Guid.NewGuid(), "NSE:S" + i, 1000 + i, false, null)).ToArray();

        var plan = Plan(Today, Small with { StockCount = 2 }, [finnifty, Nifty, .. shares]);

        plan.Select(i => i.Key.UnderlyingId).Distinct().ShouldBe([NiftyId, shares[0].InstrumentId, shares[1].InstrumentId]);
        Plan(Today, Small with { StockCount = 0 }, Nifty, Stock).ShouldAllBe(i => i.Key.UnderlyingId == NiftyId);
    }

    [Fact]
    public void A_target_that_is_missing_is_simply_not_planned()
    {
        Plan(Today, null, Stock).ShouldAllBe(i => i.Key.UnderlyingId == StockId);
        BackfillPlanner.Plan([], OptionBackfillSettings.Off, Today).ShouldBeEmpty();
    }

    [Fact]
    public void A_key_builds_the_request_with_its_window_and_the_item_carries_the_strike_step()
    {
        var item = Plan(Today, Small, Nifty)[0];

        var request = item.ToRequest();

        (request.SecurityId, request.Instrument, request.IntervalMinutes, request.ExpiryFlag, request.ExpiryCode, request.Offset, request.Side).ShouldBe((13L, "OPTIDX", 5, 'W', 1, 0, "CE"));
        (request.To.DayNumber - request.From.DayNumber).ShouldBe(30);
        request.To.ShouldBeLessThanOrEqualTo(new DateOnly(2026, 11, 1));
        item.StrikeStepPaise.ShouldBe(5_000);
    }

    [Theory]
    [InlineData(1, 0.2, 5000)]
    [InlineData(1, 1, 1000)]
    [InlineData(1, 2, 500)]
    [InlineData(1, 5, 500)]
    public void The_spacing_is_one_over_the_rate_and_never_under_half_a_second(int _, double rate, int expectedMs)
    {
        (OptionBackfillSettings.Off with { RequestsPerSecond = rate }).Spacing.TotalMilliseconds.ShouldBe(expectedMs);
    }
}
