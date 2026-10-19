using XDrishti.Domain.Research;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Verification;

namespace XDrishti.SimExport.Tests.Verification;

/// <summary>
/// Criteria 2 and 3 of P3-09: the sampler's balance on a hand-made pool, the draw pick by pick against an independent computation (Python's hashlib over the same rules, in the description of V1), the year spread, and the seed and order
/// properties.
/// </summary>
public sealed class VerificationSamplerTests
{
    // computed with an independent tool for seed s1 (the pool of PackFixtures.HandPool, without the open-at-end trade)
    private static readonly string[] SeedS1 =
    [
        "X|NSE:A|Buy|2025-04-04", "Y|NSE:A|Buy|2024-04-05", "X|NSE:B|Buy|2024-04-05", "Y|NSE:A|Buy|2025-04-05", "X|NSE:B|Buy|2025-04-04", "Y|NSE:A|Buy|2024-04-04",
        "X|NSE:A|Buy|2024-04-05", "Y|NSE:A|Buy|2025-04-04", "X|NSE:A|Buy|2025-04-05", "X|NSE:B|Buy|2024-04-04", "X|NSE:B|Buy|2025-04-05", "X|NSE:A|Buy|2024-04-04",
    ];

    private static readonly string[] SeedAbc =
    [
        "X|NSE:A|Buy|2024-04-05", "Y|NSE:A|Buy|2024-04-04", "X|NSE:B|Buy|2025-04-05", "Y|NSE:A|Buy|2025-04-04", "X|NSE:B|Buy|2024-04-04", "Y|NSE:A|Buy|2024-04-05",
        "X|NSE:A|Buy|2025-04-05", "Y|NSE:A|Buy|2025-04-05", "X|NSE:A|Buy|2024-04-04", "X|NSE:B|Buy|2025-04-04", "X|NSE:B|Buy|2024-04-05", "X|NSE:A|Buy|2025-04-04",
    ];

    private static readonly string[] Symbols = ["NSE:A", "NSE:B"];

    private static readonly string[] Other = ["NSE:P", "NSE:Q", "NSE:R"];

    private static readonly int[] Years = [2024, 2025, 2026];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(20)]
    [InlineData(1000)]
    public void The_draw_equals_the_independent_computation_pick_by_pick_and_the_whole_pool_when_it_is_smaller_than_the_count(int count)
    {
        var picks = VerificationSampler.Draw(PackFixtures.HandPool(), "s1", count);

        PackFixtures.Keys(picks).ShouldBe(SeedS1.Take(count));
        picks.Count.ShouldBe(Math.Min(count, 12));
    }

    [Fact]
    public void A_seed_that_differs_only_in_case_is_a_different_seed_with_its_own_independent_draw()
    {
        PackFixtures.Keys(VerificationSampler.Draw(PackFixtures.HandPool(), "Abc", 12)).ShouldBe(SeedAbc);
        PackFixtures.Keys(VerificationSampler.Draw(PackFixtures.HandPool(), "abc", 12)).ShouldNotBe(SeedAbc);
    }

    [Fact]
    public void No_trade_is_repeated_none_is_from_the_open_at_end_trades_and_the_strategy_instrument_and_year_counts_stay_balanced_while_both_sides_have_trades()
    {
        var pool = PackFixtures.HandPool();
        var open = pool[0].OpenAtEnd.Select(t => t.Trade.Key).ToList();
        open.Count.ShouldBe(1);

        foreach (var seed in Enumerable.Range(0, 30).Select(i => "seed" + i))
        {
            var picks = VerificationSampler.Draw(pool, seed, 100).Select(p => p.Trade.Trade).ToList();

            picks.Select(t => t.Key).Distinct().Count().ShouldBe(picks.Count, seed);
            picks.Select(t => t.Key).ShouldNotContain(open[0], seed);
            for (var n = 1; n <= picks.Count; n++)
            {
                var head = picks.Take(n).ToList();
                if (n <= 8)
                {
                    Math.Abs(head.Count(t => t.StrategyId == "X") - head.Count(t => t.StrategyId == "Y")).ShouldBeLessThanOrEqualTo(1, seed + " strategy " + n);
                }

                if (n <= 4 || head.Count(t => t.StrategyId == "Y") == 4)
                {
                    var x = head.Where(t => t.StrategyId == "X").ToList();
                    Math.Abs(x.Count(t => t.Symbol == "NSE:A") - x.Count(t => t.Symbol == "NSE:B")).ShouldBeLessThanOrEqualTo(1, seed + " instrument " + n);
                }

                foreach (var symbol in Symbols)
                {
                    var own = head.Where(t => t.StrategyId == "X" && t.Symbol == symbol).ToList();
                    if (own.Count <= 2)
                    {
                        Math.Abs(own.Count(t => t.TradeDate.Year == 2024) - own.Count(t => t.TradeDate.Year == 2025)).ShouldBeLessThanOrEqualTo(1, seed + " year " + symbol + " " + n);
                    }
                }
            }
        }
    }

    [Fact]
    public void One_strategy_is_valid_and_a_cell_with_one_trade_is_drawn_once()
    {
        var run = PackFixtures.Run("X", [PackFixtures.Neutral("X", "NSE:A", new DateOnly(2025, 4, 4))]);

        var picks = VerificationSampler.Draw([run], "s1", 5);

        PackFixtures.Keys(picks).ShouldBe(["X|NSE:A|Buy|2025-04-04"]);
    }

    [Fact]
    public void An_empty_pool_gives_an_empty_draw()
    {
        VerificationSampler.Draw([PackFixtures.Run("X", [])], "s1", 20).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("s1", 12)]
    [InlineData("p3-check-1", 12)]
    [InlineData("zz.9_x", 7)]
    public void With_enough_instruments_trading_in_three_years_the_years_of_the_picks_differ_by_at_most_one(string seed, int count)
    {
        var trades = new List<SimTrade>();
        for (var i = 0; i < 24; i++)
        {
            foreach (var year in Years)
            {
                trades.Add(PackFixtures.Neutral("X", "NSE:S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), new DateOnly(year, 4, 6)));
                trades.Add(PackFixtures.Neutral("X", "NSE:S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), new DateOnly(year, 4, 7)));
            }
        }

        var picks = VerificationSampler.Draw([PackFixtures.Run("X", trades)], seed, count).Select(p => p.Trade.Trade).ToList();

        var years = Years.Select(y => picks.Count(t => t.TradeDate.Year == y)).ToList();
        (years.Max() - years.Min()).ShouldBeLessThanOrEqualTo(1);
        picks.Select(t => t.Symbol).Distinct().Count().ShouldBe(count, "every pick takes a new instrument while there are instruments that have none");
    }

    [Fact]
    public void An_instrument_that_trades_in_one_year_only_gives_that_year_and_nothing_more_is_asserted()
    {
        var trades = new List<SimTrade>();
        for (var i = 0; i < 12; i++)
        {
            trades.Add(PackFixtures.Neutral("X", "NSE:O" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2024 + (i % 3), 4, 6)));
        }

        var picks = VerificationSampler.Draw([PackFixtures.Run("X", trades)], "s1", 12).Select(p => p.Trade.Trade).ToList();

        picks.Count.ShouldBe(12);
        foreach (var trade in picks)
        {
            trade.TradeDate.Year.ShouldBe(2024 + (int.Parse(trade.Symbol[^2..], System.Globalization.CultureInfo.InvariantCulture) % 3));
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_list_whatever_the_order_of_the_trades_or_of_the_runs_and_a_different_seed_a_different_set()
    {
        var trades = new List<SimTrade>();
        foreach (var symbol in Other)
        {
            foreach (var year in Years.Take(2))
            {
                for (var day = 1; day <= 50; day++)
                {
                    trades.Add(PackFixtures.Neutral("X", symbol, new DateOnly(year, 5, 1).AddDays(day)));
                }
            }
        }

        var a = PackFixtures.Keys(VerificationSampler.Draw([PackFixtures.Run("X", trades)], "s1", 10));
        var again = PackFixtures.Keys(VerificationSampler.Draw([PackFixtures.Run("X", trades)], "s1", 10));
        var reversed = PackFixtures.Keys(VerificationSampler.Draw([PackFixtures.Run("X", [.. trades.AsEnumerable().Reverse()])], "s1", 10));
        var other = PackFixtures.Keys(VerificationSampler.Draw([PackFixtures.Run("X", trades)], "s2", 10));

        again.ShouldBe(a);
        reversed.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(a.OrderBy(k => k, StringComparer.Ordinal));
        other.OrderBy(k => k, StringComparer.Ordinal).ShouldNotBe(a.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void The_order_of_the_runs_does_not_change_the_set()
    {
        var pool = PackFixtures.HandPool();

        var forward = PackFixtures.Keys(VerificationSampler.Draw(pool, "s1", 12)).Order(StringComparer.Ordinal);
        var backward = PackFixtures.Keys(VerificationSampler.Draw([pool[1], pool[0]], "s1", 12)).Order(StringComparer.Ordinal);

        backward.ShouldBe(forward);
        PackFixtures.Keys(VerificationSampler.Draw([pool[1], pool[0]], "s1", 12)).ShouldBe(SeedS1, "the whole list, not only the set: the draw does not read the run order");
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("naïve")]
    [InlineData("a|b")]
    [InlineData("0123456789012345678901234567890123456789x")]
    public void A_seed_that_is_not_1_to_40_characters_of_letters_digits_dot_underscore_and_hyphen_is_refused(string seed)
    {
        VerificationSampler.IsValidSeed(seed).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => VerificationSampler.Draw(PackFixtures.HandPool(), seed, 1));
    }
}
