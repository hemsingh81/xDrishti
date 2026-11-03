using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>P14-08 criteria 8 and 9 (seeded properties, 200 runs each): no look-ahead, the fallback IV never later than the bar it prices, and the same bars in any order giving the same row.</summary>
public sealed class OptionRemarkPropertyTests
{
    private const int Seeds = 200;

    private sealed record Scenario(RemarkTrade Trade, List<OptionBar> Bars, OptionProfileParams Profile, DateTimeOffset EntryTs, DateTimeOffset ExitTs, RemarkContext Context);

    private static Scenario Build(int seed)
    {
        var rng = new Random(seed);
        var steps = new int[75];
        var walk = RemarkKit.BaseSpot;
        for (var i = 0; i < steps.Length; i++)
        {
            walk += rng.Next(-900, 901);
            steps[i] = walk;
        }

        var call = rng.Next(2) == 0;
        var window = rng.Next(2) == 0 ? 10 : 3;
        var entryBar = rng.Next(0, 30);
        var exitBar = entryBar + rng.Next(1, 30);
        var skew = rng.Next(0, 5);
        var day = RemarkKit.Tuesday;
        var start = RemarkKit.At(day, 9, 15);
        var trade = RemarkKit.Trade(key: "T" + seed, buy: call, day: day, entryHour: 9, entryMinute: 15, exitHour: 9, exitMinute: 15) with
        {
            EntryEpoch = start.AddMinutes((entryBar * 5) + skew).ToUnixTimeSeconds(),
            ExitEpoch = start.AddMinutes(exitBar * 5).ToUnixTimeSeconds(),
        };
        var stop = rng.Next(2) == 0 ? (decimal?)null : rng.Next(10, 60);
        var profile = OptionProfileParams.Create(rng.Next(2) == 0 ? "atm" : "itm1", "nearest_safe", stop, 1).Value;
        var bars = RemarkKit.Day(day, call, bar => steps[bar], window: window, iv: 10f + (seed % 20));
        var entryTs = start.AddMinutes((entryBar + (skew > 0 ? 1 : 0)) * 5);
        return new Scenario(trade, bars, profile, entryTs, start.AddMinutes(exitBar * 5), RemarkKit.Context(maxIvAge: 5 + (seed % 90)));
    }

    private static bool Same(RemarkRow a, RemarkRow b) =>
        a.Fate == b.Fate && a.StrikePx == b.StrikePx && a.Expiry == b.Expiry && a.EntryTs == b.EntryTs && a.ExitTs == b.ExitTs && a.EntryPremiumPx == b.EntryPremiumPx && a.ExitPremiumPx == b.ExitPremiumPx && a.Units == b.Units
        && a.GrossInr == b.GrossInr && a.REquiv == b.REquiv && a.ExitReason == b.ExitReason && a.Lot == b.Lot && a.Sizable == b.Sizable && a.Labels.SequenceEqual(b.Labels);

    private static OptionBar Scramble(OptionBar bar, Random rng) => bar with
    {
        OpenPx = rng.Next(1, 90_000),
        HighPx = rng.Next(1, 90_000),
        LowPx = rng.Next(1, 90_000),
        ClosePx = rng.Next(1, 90_000),
        SpotPx = RemarkKit.BaseSpot + rng.Next(-100_000, 100_000),
        Iv = rng.Next(3) == 0 ? null : rng.Next(1, 90),
        Usable = rng.Next(4) != 0,
        Suspect = rng.Next(5) == 0,
    };

    [Fact]
    public void Changing_any_bar_after_the_exit_bar_changes_nothing_in_the_row()
    {
        var paired = 0;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var s = Build(seed);
            var before = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(s.Bars), s.Context);
            var rng = new Random(seed + 10_000);
            var changed = s.Bars.Select(b => b.Ts > s.ExitTs ? Scramble(b, rng) : b.Ts == s.ExitTs ? b with { HighPx = rng.Next(1, 90_000), LowPx = rng.Next(1, 90_000), ClosePx = rng.Next(1, 90_000) } : b).ToList();

            var after = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(changed), s.Context);

            Same(before, after).ShouldBeTrue($"seed {seed}: {before.Fate} {before.ExitReason} vs {after.Fate} {after.ExitReason}");
            paired += before.IsPaired ? 1 : 0;
        }

        paired.ShouldBeGreaterThan(Seeds / 2, "the property is not vacuous: most scenarios pair");
    }

    [Fact]
    public void Changing_any_bar_after_the_entry_decision_leaves_the_strike_and_the_expiry_as_chosen()
    {
        var compared = 0;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var s = Build(seed);
            var before = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(s.Bars), s.Context);
            var rng = new Random(seed + 20_000);
            var changed = s.Bars.Select(b => b.Ts > s.EntryTs ? Scramble(b, rng) : b).ToList();

            var after = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(changed), s.Context);

            if (before.IsPaired && after.IsPaired)
            {
                after.StrikePx.ShouldBe(before.StrikePx, $"seed {seed}");
                after.Expiry.ShouldBe(before.Expiry);
                after.ExpiryCode.ShouldBe(before.ExpiryCode);
                after.EntryPremiumPx.ShouldBe(before.EntryPremiumPx);
                compared++;
            }
        }

        compared.ShouldBeGreaterThan(20);
    }

    [Fact]
    public void The_fallback_iv_is_never_later_than_the_bar_it_prices_nor_older_than_the_setting_and_is_the_latest_such()
    {
        for (var seed = 0; seed < Seeds; seed++)
        {
            var rng = new Random(seed);
            var bars = RemarkKit.Day(RemarkKit.Tuesday, true, _ => RemarkKit.BaseSpot, window: 10, tweak: b => rng.Next(3) == 0 ? b with { Iv = null } : rng.Next(7) == 0 ? b with { Suspect = true } : b, iv: 12f);
            var series = new HeldStrikeSeries(BarGrid.Build(bars), RemarkKit.BaseSpot);
            var before = RemarkKit.At(RemarkKit.Tuesday, 9, 15).AddMinutes(rng.Next(0, 75) * 5);
            var maxAge = rng.Next(5, 391);

            var found = series.LatestIv(before, maxAge);

            var expected = series.Grid.Times.Where(t => t < before && (before - t).TotalMinutes <= maxAge && series.Held(t, out var held) == HeldState.Available && held!.Iv is > 0).Select(t => (DateTimeOffset?)t).LastOrDefault();
            found?.Ts.ShouldBeLessThan(before, $"seed {seed}");
            found?.Ts.ShouldBe(expected!.Value, $"seed {seed}");
            (found is null).ShouldBe(expected is null, $"seed {seed}");
        }
    }

    [Fact]
    public void The_same_bars_in_any_order_give_the_same_row_and_a_second_evaluation_changes_nothing()
    {
        for (var seed = 0; seed < Seeds; seed++)
        {
            var s = Build(seed);
            var first = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(s.Bars), s.Context);
            var shuffled = s.Bars.OrderBy(_ => new Random(seed).Next()).OrderBy(b => (b.Offset * 31) + b.Ts.Minute % 7).ToList();

            var second = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(shuffled), s.Context);
            var third = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(s.Bars), s.Context);

            Same(first, second).ShouldBeTrue($"seed {seed}");
            Same(first, third).ShouldBeTrue($"seed {seed}");
        }
    }

    [Fact]
    public void With_holes_and_truncated_sessions_a_paired_option_never_exits_before_the_cash_exit_or_the_session_close_it_stands_for()
    {
        var paired = 0;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var s = Build(seed);
            var rng = new Random(seed + 30_000);
            var cutAt = RemarkKit.At(RemarkKit.Tuesday, 9, 15).AddMinutes(rng.Next(10, 75) * 5);
            var holeAt = RemarkKit.At(RemarkKit.Tuesday, 9, 15).AddMinutes(rng.Next(0, 75) * 5);
            var bars = s.Bars.Where(b => !(b.Ts >= holeAt && b.Ts < holeAt.AddMinutes(5 * rng.Next(1, 12))) && (seed % 2 == 0 || b.Ts < cutAt)).ToList();
            var cashExit = DateTimeOffset.FromUnixTimeSeconds(s.Trade.ExitEpoch);

            var row = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(bars), s.Context);

            if (row.IsPaired && row.ExitReason != RemarkFates.PremiumStop)
            {
                row.ExitTs.ShouldBeGreaterThanOrEqualTo(cashExit, $"seed {seed}");
                paired++;
            }
        }

        paired.ShouldBeGreaterThan(Seeds / 4);
    }

    [Fact]
    public void Every_trade_gets_exactly_one_row_whatever_its_fate()
    {
        var fates = new HashSet<string>();
        for (var seed = 0; seed < Seeds; seed++)
        {
            var s = Build(seed);
            var rng = new Random(seed);
            var bars = s.Bars.Where(_ => rng.Next(4) != 0).ToList();

            var row = OptionRemark.Evaluate(s.Trade, RemarkKit.Plan, s.Profile, BarGrid.Build(bars), s.Context);

            row.TradeKey.ShouldBe(s.Trade.Key);
            (row.IsPaired || RemarkFates.Unpaired.Contains(row.Fate)).ShouldBeTrue(row.Fate);
            if (!row.IsPaired)
            {
                row.EntryPremiumPx.ShouldBeNull();
                row.REquiv.ShouldBeNull();
            }

            fates.Add(row.Fate);
        }

        fates.Count.ShouldBeGreaterThan(1);
    }
}
