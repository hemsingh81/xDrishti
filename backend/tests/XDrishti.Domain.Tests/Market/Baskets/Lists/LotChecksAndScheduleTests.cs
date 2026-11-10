using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Domain.Tests.Market.Baskets.Lists;

public sealed class LotChecksAndScheduleTests
{
    [Fact]
    public void A_lot_that_differs_from_the_master_is_a_mismatch_with_both_values_and_equal_lots_are_not()
    {
        var nse = new Dictionary<string, int> { ["RELIANCE"] = 275, ["SAIL"] = 4700, ["NEW"] = 10 };
        var master = new Dictionary<string, int> { ["RELIANCE"] = 250, ["SAIL"] = 4700 };

        LotChecks.Mismatches(nse, master).ShouldBe([new LotMismatch("RELIANCE", 250, 275)]);
    }

    [Fact]
    public void The_universe_difference_names_who_has_what_alone()
    {
        var diff = LotChecks.UniverseDiff(["A", "B"], ["A", "B", "C"]);

        diff.OnlyMaster.ShouldBe(["C"]);
        diff.OnlyNse.ShouldBeEmpty();
        diff.Differs.ShouldBeTrue();
        LotChecks.UniverseDiff(["A"], ["A"]).Differs.ShouldBeFalse();
    }

    [Fact]
    public void The_latest_scheduled_time_is_today_when_it_has_passed_and_yesterday_otherwise()
    {
        var times = new[] { new TimeOnly(19, 0), new TimeOnly(8, 30) };
        var morning = new DateTimeOffset(2026, 10, 27, 9, 0, 0, IndiaTime.Offset);
        var early = new DateTimeOffset(2026, 10, 27, 7, 45, 0, IndiaTime.Offset);

        ListSchedule.LatestScheduled(morning, times).ShouldBe(new DateTimeOffset(2026, 10, 27, 8, 30, 0, IndiaTime.Offset));
        ListSchedule.LatestScheduled(early, times).ShouldBe(new DateTimeOffset(2026, 10, 26, 19, 0, 0, IndiaTime.Offset));
    }

    [Fact]
    public void A_source_is_due_once_per_scheduled_time()
    {
        var times = new[] { new TimeOnly(19, 0), new TimeOnly(8, 30) };
        var now = new DateTimeOffset(2026, 10, 26, 19, 5, 0, IndiaTime.Offset);

        ListSchedule.IsDue(now, null, times).ShouldBeTrue();
        ListSchedule.IsDue(now, now.AddMinutes(-3), times).ShouldBeFalse(); // asked since 19:00
        ListSchedule.IsDue(now, now.AddHours(-3), times).ShouldBeTrue(); // last asked before 19:00
        ListSchedule.IsDue(now.AddHours(13), now, times).ShouldBeFalse(); // 08:05 next day: 08:30 not reached yet
        ListSchedule.IsDue(now.AddHours(13).AddMinutes(30), now, times).ShouldBeTrue(); // 08:35: the morning retry is due
    }

    [Fact]
    public void The_ids_of_the_seeded_baskets_are_the_md5_uuids_the_migration_computes()
    {
        foreach (var definition in ListBaskets.Baskets.Append(ListBaskets.LotSizes))
        {
#pragma warning disable CA5351 // md5 is the database's own uuid derivation (md5(text)::uuid), not a security use
            var hex = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("market.basket:list:" + definition.Key)));
#pragma warning restore CA5351

            definition.Id.ShouldBe(Guid.Parse(hex));
        }
    }

    [Fact]
    public void The_ban_basket_says_why_a_name_is_excluded_and_only_the_two_nse_files_are_fetched()
    {
        ListBaskets.Ban.Description.ShouldStartWith("Excluded from new positions: ban period");
        ListBaskets.Baskets.Concat([ListBaskets.LotSizes]).Where(b => ListBaskets.IsListFile(b.Format)).Select(b => b.Key).ShouldBe(["fo-ban", "fo-lot-sizes"]);
        ListBaskets.FromMaster.ShouldAllBe(b => !ListBaskets.IsListFile(b.Format));
    }

    [Fact]
    public void The_flags_of_basket_13_are_the_state_of_each_date_and_there_are_none_before_the_lists_are_known()
    {
        var since = new DateOnly(2026, 10, 20);
        var asm = new[] { new ListStay(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 25)) };
        var ban = new[] { new ListStay(new DateOnly(2026, 10, 26), null) };

        var flags = DayFlagBuilder.Build(since, new DateOnly(2026, 10, 27), "EQ", asm, ban, []);

        flags.Select(f => (f.Date.Day, f.AsmGsm, f.InBan, f.Series)).ShouldBe(
            [(20, false, false, "EQ"), (22, true, false, "EQ"), (25, false, false, "EQ"), (26, false, true, "EQ"), (28, false, false, "")]);
        flags[^1].Unknown.ShouldBeTrue(); // nothing is known after the last good check
        flags.ShouldAllBe(f => f.Date >= since);
    }

    [Fact]
    public void A_share_now_in_a_trade_for_trade_series_was_EQ_until_the_list_says_when_it_moved()
    {
        var since = new DateOnly(2026, 10, 20);
        var t2t = new[] { new ListStay(new DateOnly(2026, 10, 24), null) };

        var flags = DayFlagBuilder.Build(since, new DateOnly(2026, 10, 30), "BE", [], [], t2t);

        flags.Where(f => !f.Unknown).Select(f => (f.Date.Day, f.Series)).ShouldBe([(20, "EQ"), (24, "BE")]);
    }

    [Fact]
    public void A_series_that_is_unknown_is_never_EQ()
    {
        DayFlagBuilder.Build(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), null, [], [], []).First().Series.ShouldNotBe("EQ");
        DayFlagBuilder.Build(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), "BL", [], [], []).First().Series.ShouldBe("BL");
    }

    [Fact]
    public void Stay_changes_after_the_last_good_check_are_not_reported_and_a_range_that_ends_before_it_begins_vouches_for_nothing()
    {
        var since = new DateOnly(2026, 10, 20);
        var ban = new[] { new ListStay(new DateOnly(2026, 10, 26), null) };

        var cut = DayFlagBuilder.Build(since, new DateOnly(2026, 10, 24), "EQ", [], ban, []);
        var none = DayFlagBuilder.Build(since, new DateOnly(2026, 10, 19), "EQ", [], [], []);

        cut.Select(f => (f.Date.Day, f.InBan, f.Unknown)).ShouldBe([(20, false, false), (25, false, true)]);
        none.ShouldHaveSingleItem().Unknown.ShouldBeTrue();
    }
}
