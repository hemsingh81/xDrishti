using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criterion 2 of P3-04c: which trades get a card (C1), the seeded order and the examples A to D worked by hand.</summary>
public sealed class CardSelectionTests
{
    private static readonly (ExitReason Exit, Side Side)[] Strata =
    [
        (ExitReason.Stop, Side.Buy), (ExitReason.Stop, Side.Sell), (ExitReason.Target, Side.Buy), (ExitReason.Target, Side.Sell), (ExitReason.SquareOff, Side.Buy), (ExitReason.SquareOff, Side.Sell),
    ];

    private static readonly string[] FirstFour = ["stop-buy", "stop-sell", "target-buy", "target-sell"];

    private static readonly int[] Caps = [0, 3, 5, 6, 7, 200, 500];

    /// <summary>A run whose stratum <c>i</c> (in the order Stop-Buy, Stop-Sell, Target-Buy, Target-Sell, SquareOff-Buy, SquareOff-Sell) holds <c>sizes[i]</c> trades; every trade has its own date.</summary>
    private static SimulationRun RunOf(params int[] sizes)
    {
        var trades = new List<SimTrade>();
        var day = 0;
        for (var s = 0; s < sizes.Length; s++)
        {
            for (var n = 0; n < sizes[s]; n++)
            {
                trades.Add(Trade(Strata[s].Side, Strata[s].Exit, new DateOnly(2020, 1, 1).AddDays(day++)));
            }
        }

        return FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2019, 1, 1), new DateOnly(2040, 1, 1))], trades, from: new DateOnly(2019, 1, 1), to: new DateOnly(2040, 1, 1));
    }

    private static SimTrade Trade(Side side, ExitReason exit, DateOnly date) =>
        FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", side, date, exit);

    private static string StratumOf(SimTrade t) => ExportSpellings.Of(t.ExitReason) + "-" + ExportSpellings.Of(t.Side).ToLowerInvariant();

    /// <summary>The trades of the run in a stratum, ranked as the selection ranks them.</summary>
    private static List<string> Ranked(SimulationRun run, string stratum) =>
        [.. SeededRank.Order(run.RunKey, run.Trades.Where(t => StratumOf(t.Trade) == stratum).Select(t => t.Trade.Key))];

    private static string[] Keys(IEnumerable<CardChoice> choices) => [.. choices.Select(c => c.Trade.Trade.Key)];

    // the seeded order

    [Fact]
    public void The_rank_has_known_answers_computed_with_an_independent_tool()
    {
        // printf 'run1|<key>' | shasum -a 256
        SeededRank.Of("run1", "ORB|NSE:AAA|Buy|2026-03-02").ShouldBe("509a9e0dccbc563d082af965c7d806e986ca1f8dc1e053fe75c08c6c48f51d10");
        SeededRank.Of("run1", "ORB|NSE:AAA|Sell|2026-03-02").ShouldBe("58a7a45aa44fece74574843f4336e0b0c231ed510eb3ffcdaafa77b5ffc8ecf4");
        SeededRank.Of("run1", "ORB|NSE:M&M|Buy|2026-03-03").ShouldBe("ba2bc874269d48e8b11b62dff40b9c0bf5eaa966c67e0ef69cf55dd30eaf04f3");
        SeededRank.Order("run1", ["ORB|NSE:AAA|Sell|2026-03-02", "ORB|NSE:M&M|Buy|2026-03-03", "ORB|NSE:AAA|Buy|2026-03-02"]).ShouldBe(["ORB|NSE:AAA|Buy|2026-03-02", "ORB|NSE:AAA|Sell|2026-03-02", "ORB|NSE:M&M|Buy|2026-03-03"]);
    }

    [Fact]
    public void The_order_does_not_depend_on_the_order_of_the_input_is_the_same_for_the_same_seed_and_differs_with_another_seed()
    {
        var keys = Enumerable.Range(0, 60).Select(i => "key" + i).ToList();

        SeededRank.Order("s", keys).ShouldBe(SeededRank.Order("s", keys.AsEnumerable().Reverse()));
        SeededRank.Order("s", keys).ShouldBe(SeededRank.Order("s", keys));
        SeededRank.Order("s", keys).ShouldNotBe(SeededRank.Order("t", keys));
        SeededRank.Order("s", keys).Order(StringComparer.Ordinal).ShouldBe(keys.Order(StringComparer.Ordinal));
    }

    // the examples of C1

    [Fact]
    public void Example_A_sizes_4_1_2_0_3_0_and_cap_7_gives_round_one_of_four_and_round_two_of_three()
    {
        var run = RunOf(4, 1, 2, 0, 3, 0);

        var chosen = CardSelection.Choose(run, 7);

        var expected = new[]
        {
            Ranked(run, "stop-buy")[0], Ranked(run, "stop-sell")[0], Ranked(run, "target-buy")[0], Ranked(run, "square_off-buy")[0],
            Ranked(run, "stop-buy")[1], Ranked(run, "target-buy")[1], Ranked(run, "square_off-buy")[1],
        };
        Keys(chosen).Order(StringComparer.Ordinal).ShouldBe(expected.Order(StringComparer.Ordinal));
        chosen.Count.ShouldBe(7);
    }

    [Fact]
    public void Example_B_sizes_3_2_2_1_0_0_and_cap_5_cuts_the_second_round_after_the_first_stratum()
    {
        var run = RunOf(3, 2, 2, 1, 0, 0);

        var chosen = CardSelection.Choose(run, 5);

        var expected = new[]
        {
            Ranked(run, "stop-buy")[0], Ranked(run, "stop-sell")[0], Ranked(run, "target-buy")[0], Ranked(run, "target-sell")[0],
            Ranked(run, "stop-buy")[1],
        };
        Keys(chosen).Order(StringComparer.Ordinal).ShouldBe(expected.Order(StringComparer.Ordinal));
        Keys(chosen).ShouldNotContain(Ranked(run, "stop-sell")[1]);
        Keys(chosen).ShouldNotContain(Ranked(run, "target-buy")[1]);
    }

    [Fact]
    public void Example_C_one_stratum_of_ten_and_cap_3_gives_ranks_1_2_and_3()
    {
        var run = RunOf(10, 0, 0, 0, 0, 0);

        var chosen = CardSelection.Choose(run, 3);

        Keys(chosen).Order(StringComparer.Ordinal).ShouldBe(Ranked(run, "stop-buy").Take(3).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Example_D_six_strata_of_one_and_cap_4_gives_the_first_four_strata_in_order()
    {
        var run = RunOf(1, 1, 1, 1, 1, 1);

        var chosen = CardSelection.Choose(run, 4);

        chosen.Select(c => c.Stratum).Order(StringComparer.Ordinal).ShouldBe(FirstFour.Order(StringComparer.Ordinal));
    }

    // the properties

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(5000)]
    public void A_cap_never_gives_more_cards_than_it_says_the_whole_list_when_it_fits_no_repeated_key_and_the_runs_trade_order(int trades)
    {
        var run = RunOf(trades / 2, trades / 4, trades - (trades / 2) - (trades / 4), 0, 0, 0);

        foreach (var cap in Caps)
        {
            var chosen = CardSelection.Choose(run, cap);

            chosen.Count.ShouldBe(Math.Min(cap, trades));
            Keys(chosen).Distinct().Count().ShouldBe(chosen.Count);
            chosen.Select(c => c.Number).ShouldBe(chosen.Select(c => c.Number).Order(), "the run's trade order");
            chosen.ShouldAllBe(c => run.Trades[c.Number - 1] == c.Trade);
            if (trades <= cap)
            {
                Keys(chosen).ShouldBe(Keys(run.Trades.Select(t => new CardChoice(0, t, string.Empty))));
            }
        }
    }

    [Fact]
    public void Every_non_empty_stratum_is_in_round_one_when_the_cap_allows()
    {
        var run = RunOf(50, 40, 30, 20, 10, 5);

        var chosen = CardSelection.Choose(run, 6);

        chosen.Select(c => c.Stratum).Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public void The_same_trades_in_another_order_and_equal_runs_give_the_same_cards_and_another_run_key_gives_another_set()
    {
        var run = RunOf(60, 60, 60, 60, 60, 60);
        var shuffled = run with { Trades = [.. run.Trades.AsEnumerable().Reverse()] };
        var other = run with { RunKey = new string('9', 64) };

        Keys(CardSelection.Choose(shuffled, 30)).Order(StringComparer.Ordinal).ShouldBe(Keys(CardSelection.Choose(run, 30)).Order(StringComparer.Ordinal));
        Keys(CardSelection.Choose(run, 30)).ShouldBe(Keys(CardSelection.Choose(run with { }, 30)));
        Keys(CardSelection.Choose(other, 30)).ToHashSet().ShouldNotBe(Keys(CardSelection.Choose(run, 30)).ToHashSet());
    }

    [Fact]
    public void The_strata_are_derived_from_the_enums_so_no_trade_of_any_exit_reason_or_side_is_left_out()
    {
        foreach (var exit in Enum.GetValues<ExitReason>().Where(e => e != ExitReason.EndOfData))
        {
            foreach (var side in Enum.GetValues<Side>())
            {
                var run = FabricatedRun.Assemble(
                    FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")),
                    [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2019, 1, 1), new DateOnly(2040, 1, 1))],
                    [Trade(side, exit, new DateOnly(2020, 1, 1)), Trade(Side.Buy, ExitReason.Stop, new DateOnly(2020, 1, 2))]);

                var chosen = CardSelection.Choose(run, 1);
                var both = CardSelection.Choose(run, 2);

                chosen.Count.ShouldBe(1);
                both.Count.ShouldBe(2, "a trade of " + exit + "/" + side + " is selected when the cap allows");
                both.ShouldContain(c => c.Trade.Trade.ExitReason == exit && c.Trade.Trade.Side == side);
            }
        }

        CardSelection.StratumNames.Count.ShouldBe((Enum.GetValues<ExitReason>().Length - 1) * Enum.GetValues<Side>().Length + 1);
        CardSelection.StratumNames[^1].ShouldBe("other");
    }

    [Fact]
    public void A_value_outside_the_known_set_goes_to_the_last_stratum_and_is_never_dropped()
    {
        var odd = Trade(Side.Buy, (ExitReason)99, new DateOnly(2020, 1, 1));
        var run = FabricatedRun.Assemble(
            FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")),
            [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2019, 1, 1), new DateOnly(2040, 1, 1))],
            [odd, Trade(Side.Buy, ExitReason.Stop, new DateOnly(2020, 1, 2)), Trade(Side.Sell, (ExitReason)(-1), new DateOnly(2020, 1, 3))]);

        var chosen = CardSelection.Choose(run, 3);

        chosen.Count.ShouldBe(3);
        chosen.Count(c => c.Stratum == "other").ShouldBe(2);
        CardSelection.Choose(run, 2).Select(c => c.Stratum).Distinct().Count().ShouldBe(2, "round one gives the last stratum its place too");
    }

    [Fact]
    public void End_of_data_trades_are_never_selected_even_when_one_is_among_the_trades()
    {
        var run = RunOf(2, 0, 0, 0, 0, 0);
        var withOpen = run with { Trades = [.. run.Trades, new RunTrade(Trade(Side.Buy, ExitReason.EndOfData, new DateOnly(2021, 1, 1)), [])] };

        var chosen = CardSelection.Choose(withOpen, 500);

        chosen.Count.ShouldBe(2);
        chosen.ShouldNotContain(c => c.Trade.Trade.ExitReason == ExitReason.EndOfData);
    }

    [Fact]
    public void A_negative_cap_is_refused_and_a_run_without_trades_gives_nothing()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CardSelection.Choose(RunOf(1, 0, 0, 0, 0, 0), -1));

        CardSelection.Choose(RunOf(0, 0, 0, 0, 0, 0), 200).ShouldBeEmpty();
    }
}
