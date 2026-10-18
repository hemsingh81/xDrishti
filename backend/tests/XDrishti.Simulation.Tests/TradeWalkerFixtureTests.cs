using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>The fixture days F1 to F17b of the spec (D9) through the real walker (AC2, AC4, AC5): every number is worked out by hand in <see cref="Cases"/>.</summary>
public sealed class TradeWalkerFixtureTests
{
    public static TheoryData<string> Days => [.. Cases.FDays.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Days))]
    public void A_hand_made_candidate_gives_exactly_the_trade_the_table_says(string name)
    {
        var walkCase = Cases.F(name);

        foreach (var item in walkCase.Items)
        {
            WalkAssert.Matches(walkCase, item, WalkAssert.Walk(walkCase, item));
        }
    }

    [Fact]
    public void The_key_of_the_trade_of_F1_is_strategy_symbol_side_and_date()
    {
        var f1 = Cases.F("F1");

        var trade = WalkAssert.Walk(f1, f1.Items[0]).Value.Trade.ShouldNotBeNull();

        trade.Key.ShouldBe("ORB|TEST|Buy|2026-03-02");
    }

    [Fact]
    public void Two_candidates_on_one_list_give_two_trades_one_per_side()
    {
        var f13 = Cases.F("F13");

        var trades = f13.Items.Select(i => WalkAssert.Walk(f13, i).Value.Trade.ShouldNotBeNull()).ToList();

        trades.Select(t => t.Side).ShouldBe([Side.Buy, Side.Sell]);
        trades.Select(t => t.ExitReason).ShouldBe([ExitReason.Stop, ExitReason.SquareOff]);
        trades.Select(t => t.Key).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void The_counters_over_the_fixtures_equal_the_hand_count_of_the_tables()
    {
        var total = WalkCounters.Empty;
        foreach (var walkCase in Cases.FDays)
        {
            foreach (var item in walkCase.Items)
            {
                total = total.Add(WalkAssert.Walk(walkCase, item).Value.ToCounters());
            }
        }

        total.ShouldBe(new WalkCounters(
            Candidates: 17,
            Trades: 15,
            NoFillBar: 1,
            AfterSquareOff: 0,
            EntryThroughStop: 1,
            ExitsStop: 4,
            ExitsTarget: 4,
            ExitsSquareOff: 6,
            ExitsEndOfData: 1,
            StopGapExits: 0,
            TargetGapExits: 0,
            EntriesAfterMissingMinutes: 2,
            BarsExamined: 2683,
            LimitNotReached: 0,
            EntriesAtOpen: 15,
            EntriesAtLimit: 0,
            AmbiguousMinutes: 1,
            LateSquareOffs: 1,
            ZeroVolumeMinutes: 0,
            RiskUnderFourTicks: 0));
    }

    [Fact]
    public void The_identities_of_the_counters_hold_on_every_fixture_and_row()
    {
        foreach (var walkCase in Cases.All)
        {
            foreach (var item in walkCase.Items)
            {
                var c = WalkAssert.Walk(walkCase, item).Value.ToCounters();

                c.Candidates.ShouldBe(c.Trades + c.NoFillBar + c.AfterSquareOff + c.EntryThroughStop + c.LimitNotReached, walkCase.Name);
                c.Trades.ShouldBe(c.ExitsStop + c.ExitsTarget + c.ExitsSquareOff + c.ExitsEndOfData, walkCase.Name);
                c.Trades.ShouldBe(c.EntriesAtOpen + c.EntriesAtLimit, walkCase.Name);
            }
        }
    }

    [Theory]
    [InlineData("F1", 86)]
    [InlineData("F5", 331)]
    [InlineData("F17", 85)]
    [InlineData("F17b-first", 81)]
    public void The_minutes_examined_are_those_of_the_table(string name, int minutes)
    {
        var walkCase = Cases.F(name);

        WalkAssert.Walk(walkCase, walkCase.Items[0]).Value.BarsExamined.ShouldBe(minutes);
    }
}
