using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Verification;

namespace XDrishti.SimExport.Tests.Verification;

/// <summary>Criteria 5 and 6 of P3-09: the cover rule on a pool built so that both outcomes are stated by construction, and the spot checks.</summary>
public sealed class PackPlannerTests
{
    private static readonly DateOnly Day = new(2025, 4, 8);

    private static readonly string[] TwoSymbols = ["NSE:S1", "NSE:S2"];

    private static SimTrade Neutral(string symbol, int offset, Side side = Side.Buy) => PackFixtures.Neutral("ORB", symbol, Day.AddDays(offset), side);

    /// <summary>
    /// Categories 1, 2, 3 and 10 occur: SELL (category 1) is in the random part; SHARED belongs to 2 and 3 and is the only trade of 3; GAPA and GAPB belong to 2 only; HOLD belongs to 10.
    /// </summary>
    private static (SimulationRun Run, SampledTrade Sell, SimTrade Shared, SimTrade GapA, SimTrade GapB, SimTrade Hold) Pool()
    {
        var sell = Neutral("NSE:S1", 0, Side.Sell);
        var shared = Neutral("NSE:S2", 1) with { ExitPrice = 97 };
        shared = shared with { ExitEpochSeconds = shared.EntryEpochSeconds };
        var gapA = Neutral("NSE:S3", 2) with { ExitPrice = 97 };
        var gapB = Neutral("NSE:S4", 3) with { ExitPrice = 96 };
        var hold = Neutral("NSE:S5", 4) with { ExitReason = ExitReason.MaxHold, ExitPrice = 100 };
        var filler = Neutral("NSE:S6", 5);
        var run = PackFixtures.Run("ORB", [sell, shared, gapA, gapB, hold, filler]);
        return (run, new SampledTrade(0, run.Trades.Single(t => t.Trade.Key == sell.Key || t.Trade.Symbol == sell.Symbol)), shared, gapA, gapB, hold);
    }

    [Fact]
    public void The_edge_picks_follow_the_table_order_and_state_both_outcomes_by_construction_for_the_shared_trade()
    {
        var (run, sell, shared, gapA, gapB, hold) = Pool();
        var outcomes = new HashSet<string>();

        foreach (var seed in Enumerable.Range(0, 40).Select(i => "seed" + i))
        {
            var plan = PackPlanner.Complete([run], seed, [sell]);

            var lowest = new[] { shared, gapA, gapB }.OrderBy(t => SeededRank.Of(seed, "edge|stop_gap|" + t.Key), StringComparer.Ordinal).First();
            var expected = lowest.Key == shared.Key ? new[] { shared.Key, hold.Key } : [lowest.Key, shared.Key, hold.Key];
            var edge = plan.Rows.Where(r => r.Source == PackPlanner.EdgeSource).Select(r => r.Trade.Trade.Key).ToList();

            edge.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(expected.OrderBy(k => k, StringComparer.Ordinal), seed);
            plan.Rows.Count(r => r.Source == PackPlanner.RandomSource).ShouldBe(1, seed);
            plan.Rows.Select(r => r.Trade.Trade.Key).Distinct().Count().ShouldBe(plan.Rows.Count, seed + " no pick repeats a random trade");
            plan.Rows.Count.ShouldBeLessThanOrEqualTo(1 + EdgeCategories.Codes.Count);
            outcomes.Add(lowest.Key == shared.Key ? "shared first" : "shared second");
        }

        outcomes.ShouldBe(["shared first", "shared second"], ignoreOrder: true, customMessage: "the seeds reach both outcomes");
    }

    [Fact]
    public void A_category_that_does_not_occur_is_false_and_adds_nothing_and_a_category_the_random_part_holds_needs_no_extra_trade()
    {
        var (run, sell, _, _, _, _) = Pool();

        var plan = PackPlanner.Complete([run], "s1", [sell]);

        plan.Occurs["sell"].ShouldBeTrue();
        plan.Occurs["stop_gap"].ShouldBeTrue();
        plan.Occurs["stop_in_entry_minute"].ShouldBeTrue();
        plan.Occurs["exit_max_hold"].ShouldBeTrue();
        plan.Occurs["overnight_entry"].ShouldBeFalse();
        plan.Occurs["adjusted_instrument"].ShouldBeFalse();
        plan.Occurs.Keys.ShouldBe(EdgeCategories.Codes, ignoreOrder: true);
        plan.Rows.Where(r => r.Source == PackPlanner.EdgeSource).SelectMany(r => r.Categories).ShouldNotContain("sell", "category 1 was already covered by the random trade");
    }

    [Fact]
    public void The_same_seed_gives_the_same_pack_and_a_different_seed_a_different_edge_pick_when_a_category_has_at_least_five_trades()
    {
        var trades = Enumerable.Range(0, 12).Select(i => Neutral("NSE:T" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), i) with { ExitReason = ExitReason.MaxHold, ExitPrice = 100 }).ToList();
        var run = PackFixtures.Run("ORB", trades);

        var a = PackPlanner.Plan([run], "s1", 0).Rows.Select(r => r.Trade.Trade.Key).ToList();
        var again = PackPlanner.Plan([run], "s1", 0).Rows.Select(r => r.Trade.Trade.Key).ToList();
        var others = Enumerable.Range(0, 10).Select(i => PackPlanner.Plan([run], "other" + i, 0).Rows.Single().Trade.Trade.Key).Distinct().ToList();

        again.ShouldBe(a);
        a.Count.ShouldBe(1, "one category occurs and no random trade was asked for");
        others.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void The_rows_are_numbered_from_one_in_a_seeded_order_that_does_not_say_which_trade_was_drawn_and_which_was_picked_and_carry_the_tick()
    {
        var (run, _, _, _, _, _) = Pool();

        var plan = PackPlanner.Plan([run], "s1", 2);

        plan.Rows.Select(r => r.Seq).ShouldBe(Enumerable.Range(1, plan.Rows.Count));
        plan.Rows.ShouldAllBe(r => r.Tick == 0.05m);
        plan.Rows.Select(r => r.Source).Distinct().ShouldBe([PackPlanner.RandomSource, PackPlanner.EdgeSource], ignoreOrder: true);
        PackPlanner.Plan([run], "s1", 2).Rows.Select(r => r.Trade.Trade.Key).ShouldBe(plan.Rows.Select(r => r.Trade.Trade.Key));
    }

    [Fact]
    public void A_pool_of_no_trades_gives_no_rows_and_no_category_occurs()
    {
        var plan = PackPlanner.Plan([PackFixtures.Run("ORB", [])], "s1", 20);

        plan.Rows.ShouldBeEmpty();
        plan.Occurs.Values.ShouldAllBe(v => !v);
    }

    [Fact]
    public void Spot_lines_exist_for_each_kind_that_occurs_none_for_a_kind_that_does_not_and_are_chosen_by_the_seeded_rank()
    {
        var open = Neutral("NSE:S1", 9);
        InstrumentRun Noted(InstrumentRun i) => i with
        {
            Notes =
            [
                new SessionNote(new DateOnly(2025, 11, 1), SessionExclusion.SpecialSession, 60, 0),
                new SessionNote(new DateOnly(2025, 9, 3), SessionExclusion.ShortSession, 300, 375),
                new SessionNote(new DateOnly(2025, 9, 4), SessionExclusion.ClosedDay, 0, 0),
            ],
            Discontinuities = [new DiscontinuityNote(new DateOnly(2025, 6, 2), 0.52)],
        };
        var run = PackFixtures.Run("ORB", [Neutral("NSE:S1", 0), Neutral("NSE:S2", 1)], [open], Noted);

        var spots = SpotChecks.Choose([run], "s1", 5);

        spots.Select(s => s.Kind + ":" + s.Detail).ShouldBe(["excluded_session:special_session", "excluded_session:short_session", "flagged_day:", "open_at_end:"]);
        spots.Select(s => s.Seq).ShouldBe([5, 6, 7, 8]);
        spots.Select(s => s.Symbol).ShouldAllBe(s => s == "NSE:S1" || s == "NSE:S2");
        var special = spots[0];
        TwoSymbols.OrderBy(sym => SeededRank.Of("s1", "spot|excluded_session|special_session|" + sym + "|2025-11-01"), StringComparer.Ordinal).First().ShouldBe(special.Symbol);
        spots[3].Key.ShouldBe("spot|open_at_end|NSE:S1|2025-04-17");
        spots.ShouldAllBe(s => s.Question.Length > 0);
        SpotChecks.Choose([run], "s1", 5).ShouldBe(spots);
    }

    [Fact]
    public void No_spot_line_is_made_for_a_run_with_none_of_the_three_kinds_and_a_spot_line_has_no_card_entry()
    {
        var run = PackFixtures.Run("ORB", [Neutral("NSE:S1", 0)]);

        SpotChecks.Choose([run], "s1", 2).ShouldBeEmpty();
        var plan = PackPlanner.Plan([PackFixtures.Run("ORB", [Neutral("NSE:S1", 0)], [Neutral("NSE:S1", 3)])], "s1", 5);
        plan.Spots.Single().Kind.ShouldBe(SpotChecks.OpenAtEnd);
        plan.Rows.ShouldAllBe(r => r.Trade.Trade.ExitReason != ExitReason.EndOfData);
    }
}
