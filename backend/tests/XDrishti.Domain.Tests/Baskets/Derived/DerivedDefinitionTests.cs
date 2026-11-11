using XDrishti.Domain.Baskets.Derived;
using static XDrishti.Domain.Tests.Baskets.Derived.DerivedFixtures;

namespace XDrishti.Domain.Tests.Baskets.Derived;

/// <summary>One scenario per definition with the hand-worked numbers of the spec (BK-02 acceptance criteria 1 to 13, 18).</summary>
public sealed class DerivedDefinitionTests
{
    private static DateOnly Last(Bar[] bars) => bars[^1].Date;

    private static bool IsMember(DerivedResult r, string symbol) => r.Members.Any(m => m.Symbol == symbol);

    [Fact]
    public void The_catalogue_holds_the_14_definitions_each_with_a_what_it_is_not_line()
    {
        DerivedCatalogue.All.Select(d => d.Id).ShouldBe(Enumerable.Range(1, 14));
        DerivedCatalogue.All.ShouldAllBe(d => d.NotThis.Length > 0 && d.Description.Length > 0 && d.Name.Length > 0);
    }

    [Fact]
    public void Each_definition_names_the_unit_its_score_is_printed_in()
    {
        DerivedCatalogue.All.Where(d => d.Unit == "percent").Select(d => d.Id).ShouldBe([1, 2, 4, 6, 10, 12]);
        DerivedCatalogue.All.Where(d => d.Unit == "rupees").Select(d => d.Id).ShouldBe([3, 13]);
        DerivedCatalogue.All.Where(d => d.Unit == "ratio").Select(d => d.Id).ShouldBe([5, 7, 8, 9, 11, 14]);
    }

    [Fact]
    public void Basket_1_ranks_by_atr_percent_and_names_the_instrument_with_59_bars_insufficient_history()
    {
        var wide = Inst("WIDE", Flat(60, 100, 2));
        var calm = Inst("CALM", Flat(60, 100, 1));
        var young = Inst("YOUNG", Flat(59, 100, 5));
        var p = Params(("Basket1.TopK", 1));

        var r = Eval(1, [calm, young, wide], Last(wide.Daily), p);

        r.Members.Select(m => m.Symbol).ShouldBe(["WIDE"]);
        Score(r, "WIDE", s => s.Score).ShouldBe(0.04, 1e-12);
        Score(r, "CALM", s => s.Score).ShouldBe(0.02, 1e-12);
        Score(r, "CALM", s => s.Rank).ShouldBe(2);
        r.Excluded.ShouldBe([new DerivedExclusion("YOUNG", "insufficient_history")]);
        r.BelowThreshold.ShouldBe(["CALM"]);
        (r.Members.Count + r.BelowThreshold.Count + r.Excluded.Count).ShouldBe(3);
    }

    [Fact]
    public void Basket_2_scores_the_mean_absolute_return_of_the_last_5_sessions()
    {
        var closes = Enumerable.Repeat(100.0, 25).ToList();
        closes.AddRange([102, 100.98, 104.0094, 101.929212, 102.94850412]);
        var a = Inst("A", FromCloses(closes));

        var r = Eval(2, [a], Last(a.Daily));

        Score(r, "A", s => s.Score).ShouldBe((0.02 + 0.01 + 0.03 + 0.02 + 0.01) / 5, 1e-9);
    }

    [Fact]
    public void Basket_3_uses_the_median_so_a_block_day_does_not_make_an_instrument_liquid()
    {
        var spike = Flat(60, 100, 0, 1_000_000);
        spike[59] = spike[59] with { Volume = 200_000_000 };
        var steady = Flat(60, 100, 0, 1_500_000);

        var r = Eval(3, [Inst("SPIKE", spike), Inst("STEADY", steady)], Last(spike), Params(("Basket3.TopK", 1)));

        r.Members.Select(m => m.Symbol).ShouldBe(["STEADY"]);
        Score(r, "SPIKE", s => s.Score).ShouldBe(100_000_000);
    }

    [Fact]
    public void Basket_4_refuses_to_run_without_a_formation_time()
    {
        var result = DerivedCatalogue.Evaluator(4).Evaluate([], Day(0), DerivedParams.Default());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("derived.formation_time_required");
    }

    [Theory]
    [InlineData(103.0, true)]
    [InlineData(102.05, false)]
    [InlineData(102.1, true)]
    public void Basket_4_gap_needs_to_reach_the_larger_of_2_percent_and_1_5_atr_percent_inclusive(double open, bool member)
    {
        var daily = FromCloses(Enumerable.Repeat(100.0, 45).ToList(), 0.7); // ATR 1.4, ATR% 1.4 %: bar = max(2 %, 2.1 %)
        var intraday = new List<IntradayBar>();
        foreach (var day in new[] { 43, 44 })
        {
            intraday.AddRange(Session(day, 5, volume: 100));
        }

        intraday.AddRange(Session(45, 10, open: open, high: open, low: open, volume: 100));
        var formation = At(45, 9, 20);

        var r = Eval(4, [Inst("G", daily, intraday: [.. intraday])], Last(daily), null, null, formation);

        IsMember(r, "G").ShouldBe(member);
        Score(r, "G", s => s.Score).ShouldBe((open / 100) - 1, 1e-9);
    }

    [Fact]
    public void Basket_4_needs_first_five_minute_volume_at_least_the_prior_median_and_ignores_bars_from_the_formation_time()
    {
        var daily = FromCloses(Enumerable.Repeat(100.0, 45).ToList(), 0.7);
        List<IntradayBar> bars = [.. Session(43, 5, volume: 100), .. Session(44, 5, volume: 100)];
        bars.AddRange(Session(45, 5, open: 103, high: 103, low: 103, volume: 10)); // 50 < 500
        var formation = At(45, 9, 20);

        var thin = Eval(4, [Inst("G", daily, intraday: [.. bars])], Last(daily), null, null, formation);
        bars.AddRange(Session(45, 10, open: 103, high: 103, low: 103, volume: 10_000).Skip(5)); // from 09:20: not known yet
        var stillThin = Eval(4, [Inst("G", daily, intraday: [.. bars])], Last(daily), null, null, formation);

        IsMember(thin, "G").ShouldBeFalse();
        IsMember(stillThin, "G").ShouldBeFalse();
    }

    [Fact]
    public void Basket_5_beta_of_1_5_is_in_and_beta_of_exactly_1_is_out()
    {
        var market = new[] { 0.01, -0.01, 0.02, -0.02 };
        var bench = FromReturns(5, 1000, market);
        var high = FromReturns(5, 100, market.Select(m => m * 1.5).ToArray());
        var one = FromReturns(5, 100, market);
        var p = Params(("Basket5.MinBars", 3));

        var r = Eval(5, [Inst("HIGH", high, bench), Inst("ONE", one, bench)], Last(bench), p);

        Score(r, "HIGH", s => s.Score).ShouldBe(1.5, 1e-9);
        IsMember(r, "HIGH").ShouldBeTrue();
        IsMember(r, "ONE").ShouldBeFalse();
        r.BelowThreshold.ShouldBe(["ONE"]);
    }

    [Fact]
    public void Basket_5_without_a_benchmark_is_insufficient_history()
    {
        var r = Eval(5, [Inst("X", Flat(260, 100))], Day(259));

        r.Excluded.ShouldBe([new DerivedExclusion("X", "insufficient_history")]);
    }

    [Fact]
    public void Basket_6_momentum_is_30_percent_and_6_1_is_18_18_percent_and_251_bars_is_named_insufficient_for_12_1()
    {
        double[] Closes(int n)
        {
            var c = Enumerable.Repeat(100.0, n).ToArray();
            c[n - 252 < 0 ? 0 : n - 252] = 100;
            c[n - 126] = 110;
            c[n - 21] = 130;
            return c;
        }

        var full = Inst("FULL", FromCloses(Closes(252)));
        var short1 = Inst("SHORT", FromCloses(Closes(251)));
        var asOf = Day(251);
        var sixOne = Params(("Basket6.Variant", 1));

        var twelve = Eval(6, [full, short1], asOf);
        var six = Eval(6, [full, short1], asOf, sixOne);

        Score(twelve, "FULL", s => s.Score).ShouldBe(0.30, 1e-12);
        twelve.Excluded.ShouldBe([new DerivedExclusion("SHORT", "insufficient_history")]);
        Score(six, "FULL", s => s.Score).ShouldBe((130.0 / 110) - 1, 1e-12);
        Score(six, "SHORT", s => s.Score).ShouldBe((130.0 / 110) - 1, 1e-12);
        six.Excluded.ShouldBeEmpty();
    }

    [Fact]
    public void Basket_7_needs_all_three_legs_and_ranks_by_variance_ratio_ascending()
    {
        var reverting = Inst("REV", FromReturns(260, 100, 0.01, -0.0099));
        var trending = Inst("TREND", FromReturns(260, 100, 0.02, 0.02, 0.02, -0.01, -0.01, -0.01));

        var r = Eval(7, [reverting, trending], Day(259));

        IsMember(r, "REV").ShouldBeTrue();
        IsMember(r, "TREND").ShouldBeFalse();
        Score(r, "REV", s => s.Score).ShouldBeLessThan(0.85);
        Score(r, "REV", s => s.Rank).ShouldBe(1);
        Score(r, "TREND", s => s.Rank).ShouldBe(0);
    }

    [Fact]
    public void Basket_8_trending_needs_er_above_half_and_adx_above_25()
    {
        var climb = Inst("UP", FromCloses(Enumerable.Range(0, 70).Select(i => 100.0 + i).ToList(), 0.5));
        var chop = Inst("CHOP", FromReturns(70, 100, 0.01, -0.0099));

        var r = Eval(8, [climb, chop], Day(69));

        IsMember(r, "UP").ShouldBeTrue();
        Score(r, "UP", s => s.Score).ShouldBe(1, 1e-9);
        IsMember(r, "CHOP").ShouldBeFalse();
    }

    private static Bar[] Highs(double lastClose, double sma, double volume)
    {
        var closes = Enumerable.Repeat(sma, 251).Append(lastClose).ToList();
        var bars = FromCloses(closes, 0, volume);
        bars[10] = bars[10] with { High = 100 };
        return bars;
    }

    [Theory]
    [InlineData(97.0, true)]
    [InlineData(96.99, false)]
    public void Basket_9_ratio_of_0_97_is_in_inclusive_and_just_below_is_out(double lastClose, bool member)
    {
        var bars = Highs(lastClose, 90, 3_000_000);

        var r = Eval(9, [Inst("H", bars)], Last(bars));

        IsMember(r, "H").ShouldBe(member);
    }

    [Fact]
    public void Basket_9_a_stock_below_its_200_day_average_is_out_whatever_the_ratio()
    {
        var bars = Highs(97, 120, 3_000_000).Select(b => b with { High = 100 }).ToArray();

        var r = Eval(9, [Inst("H", bars)], Last(bars));

        Score(r, "H", s => s.Score).ShouldBe(0.97, 1e-12);
        IsMember(r, "H").ShouldBeFalse();
    }

    [Fact]
    public void Basket_10_ranks_low_volatility_first_among_the_most_liquid_only()
    {
        var a = Inst("A", FromReturns(260, 100, 0.01, -0.01));
        var b = Inst("B", FromReturns(260, 100, 0.015, -0.015));
        var c = Inst("C", FromReturns(260, 100, 0.005, -0.005).Select(x => x with { Volume = 10 }).ToArray()); // calm but illiquid
        var p = Params(("Basket10.AdtvTop", 2), ("Basket10.TopK", 1));

        var r = Eval(10, [a, b, c], Day(259), p);

        r.Members.Select(m => m.Symbol).ShouldBe(["A"]);
        Score(r, "A", s => s.Score).ShouldBeLessThan(Score(r, "B", s => s.Score));
        Score(r, "A", s => s.Score).ShouldBe(0.01 * 15.8745, 0.002);
        Score(r, "C", s => s.Rank).ShouldBe(0);
    }

    [Theory]
    [InlineData(3_000_000, 100, true, 2.5)]
    [InlineData(2_400_000, 100, true, 2.0)]
    [InlineData(3_000_000, 49, false, 2.5)]
    [InlineData(2_300_000, 100, false, 1.9166667)]
    public void Basket_11_relative_volume_of_2_is_in_inclusive_and_a_price_of_49_is_out(double lastVolume, double price, bool member, double rvol)
    {
        var bars = Flat(45, price, 0, 1_200_000);
        bars[^1] = bars[^1] with { Volume = lastVolume };

        var r = Eval(11, [Inst("V", bars)], Last(bars));

        IsMember(r, "V").ShouldBe(member);
        Score(r, "V", s => s.Score).ShouldBe(rvol, 1e-6);
    }

    private static List<IntradayBar> OpeningRanges(double priorHigh, double priorLow, double todayHigh, double todayLow, int priorDays)
    {
        var bars = new List<IntradayBar>();
        for (var d = 0; d < priorDays; d++)
        {
            bars.AddRange(Session(20 + d, 15, open: 1000, high: priorHigh, low: priorLow));
        }

        bars.AddRange(Session(20 + priorDays, 15, open: 1000, high: todayHigh, low: todayLow));
        return bars;
    }

    [Theory]
    [InlineData(1004, 996, true)] // median 0.8 %: threshold 1.2 %, today 1.5 %
    [InlineData(1006, 994, false)] // median 1.2 %: threshold 1.8 %
    public void Basket_12_opening_range_of_1_5_percent_is_wide_only_against_a_small_enough_median(double priorHigh, double priorLow, bool member)
    {
        var bars = OpeningRanges(priorHigh, priorLow, 1010, 995, 15);
        var daily = Flat(30, 1000);

        var r = Eval(12, [Inst("O", daily, intraday: [.. bars])], Day(34), null, null, At(35, 9, 30));

        IsMember(r, "O").ShouldBe(member);
        Score(r, "O", s => s.Score).ShouldBe(0.015, 1e-12);
    }

    [Fact]
    public void Basket_12_with_a_missing_minute_in_todays_window_is_insufficient_never_a_range_of_what_is_there()
    {
        var bars = OpeningRanges(1004, 996, 1010, 995, 15).Where(b => b.Time != At(35, 9, 22)).ToList();

        var r = Eval(12, [Inst("O", Flat(30, 1000), intraday: [.. bars])], Day(34), null, null, At(35, 9, 30));

        r.Excluded.ShouldBe([new DerivedExclusion("O", "insufficient_history")]);
        r.Members.ShouldBeEmpty();
    }

    [Fact]
    public void Basket_12_prior_sessions_with_a_missing_minute_do_not_vote_so_fourteen_complete_ones_are_too_few()
    {
        var bars = OpeningRanges(1004, 996, 1010, 995, 15).Where(b => b.Time != At(20, 9, 20)).ToList();

        var r = Eval(12, [Inst("O", Flat(30, 1000), intraday: [.. bars])], Day(34), null, null, At(35, 9, 30));

        r.Excluded.ShouldBe([new DerivedExclusion("O", "insufficient_history")]);
    }

    [Fact]
    public void Basket_4_with_the_0917_minute_missing_today_is_insufficient_and_a_prior_session_with_a_missing_minute_does_not_vote()
    {
        var daily = FromCloses(Enumerable.Repeat(100.0, 45).ToList(), 0.7);
        var prior = Enumerable.Range(43, 2).SelectMany(d => Session(d, 5, volume: 100)).Where(b => b.Time != At(43, 9, 17)).ToList(); // day 43 does not vote: the median is day 44's 500
        var today = Session(45, 5, open: 103, high: 103, low: 103, volume: 200); // 1,000 >= 500
        var formation = At(45, 9, 20);

        var voting = Eval(4, [Inst("G", daily, intraday: [.. prior, .. today])], Last(daily), null, null, formation);
        var gap = Eval(4, [Inst("G", daily, intraday: [.. prior, .. today.Where(b => b.Time != At(45, 9, 17))])], Last(daily), null, null, formation);

        IsMember(voting, "G").ShouldBeTrue();
        gap.Excluded.ShouldBe([new DerivedExclusion("G", "insufficient_history")]);
    }

    [Fact]
    public void Basket_12_with_fewer_than_15_prior_sessions_is_insufficient_history()
    {
        var bars = OpeningRanges(1004, 996, 1010, 995, 14);

        var r = Eval(12, [Inst("O", Flat(30, 1000), intraday: [.. bars])], Day(33), null, null, At(34, 9, 30));

        r.Excluded.ShouldBe([new DerivedExclusion("O", "insufficient_history")]);
    }

    private static List<IntradayBar> Sessions13(int zeros)
    {
        var bars = new List<IntradayBar>();
        for (var d = 0; d < 15; d++)
        {
            bars.AddRange(Session(10 + d, 20, volume: 100));
        }

        for (var z = 0; z < zeros; z++)
        {
            bars[z] = bars[z] with { Volume = 0 };
        }

        return bars;
    }

    [Theory]
    [InlineData(15, true)] // floor(0.05 x 300) = 15
    [InlineData(16, false)]
    public void Basket_13_allows_floor_5_percent_zero_volume_minutes_and_one_more_fails(int zeros, bool member)
    {
        var daily = Flat(30, 100);
        var flags = new[] { new DayFlag(Start, false, false, "EQ") };

        var r = Eval(13, [Inst("T", daily, intraday: [.. Sessions13(zeros)], flags: flags)], Day(29));

        IsMember(r, "T").ShouldBe(member);
    }

    [Theory]
    [InlineData(true, false, "EQ")]
    [InlineData(false, true, "EQ")]
    [InlineData(false, false, "BE")]
    public void Basket_13_names_a_flagged_instrument_not_in_universe_filter(bool asm, bool ban, string series)
    {
        var flags = new[] { new DayFlag(Start, asm, ban, series) };

        var r = Eval(13, [Inst("T", Flat(30, 100), intraday: [.. Sessions13(0)], flags: flags)], Day(29));

        r.Excluded.ShouldBe([new DerivedExclusion("T", "not_in_universe_filter")]);
    }

    [Fact]
    public void Basket_13_uses_the_flag_known_at_as_of_not_a_later_one()
    {
        DayFlag[] flags = [new(Start, false, false, "EQ"), new(Day(40), true, false, "EQ")];

        var r = Eval(13, [Inst("T", Flat(30, 100), intraday: [.. Sessions13(0)], flags: flags)], Day(29));

        IsMember(r, "T").ShouldBeTrue();
    }

    private static List<InstrumentBars> ThreeGroups(int groupSize, int bars, Random rng)
    {
        var bench = Walk(rng, bars, 1000, 0.008);
        var factors = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, bars).Select(_ => (rng.NextDouble() - 0.5) * 0.04).ToArray()).ToArray();
        var universe = new List<InstrumentBars>();
        for (var g = 0; g < 3; g++)
        {
            for (var k = 0; k < groupSize; k++)
            {
                var close = 100.0;
                var daily = new Bar[bars];
                for (var i = 0; i < bars; i++)
                {
                    var market = (bench[i].Close / (i == 0 ? bench[0].Open : bench[i - 1].Close)) - 1;
                    close *= 1 + market + factors[g][i] + ((rng.NextDouble() - 0.5) * 0.002);
                    daily[i] = new Bar(Day(i), close, close, close, close, 1000);
                }

                universe.Add(Inst($"G{g}{k}", daily, bench, industry: $"Industry{g}"));
            }
        }

        return universe;
    }

    [Fact]
    public void Basket_14_groups_instruments_that_move_together_names_clusters_by_industry_and_is_stable_on_repeat()
    {
        var universe = ThreeGroups(6, 60, new Random(7));
        var p = Params(("Basket14.Window", 40), ("Basket14.MinBars", 30), ("Basket14.Clusters", 3));

        var first = Eval(14, universe, Day(59), p);
        var second = Eval(14, universe, Day(59), p, first);

        first.Members.Count.ShouldBe(18);
        foreach (var g in new[] { 0, 1, 2 })
        {
            first.Clusters.Where(c => c.Symbol.StartsWith($"G{g}", StringComparison.Ordinal)).Select(c => c.Cluster).Distinct().Count().ShouldBe(1);
            first.Clusters.First(c => c.Symbol.StartsWith($"G{g}", StringComparison.Ordinal)).Name.ShouldBe($"Industry{g}");
        }

        first.Clusters.Select(c => c.Cluster).Distinct().Count().ShouldBe(3);
        second.Stability.ShouldBe(1.0);
    }

    [Fact]
    public void Basket_14_lists_a_cluster_below_the_minimum_size_as_small_cluster()
    {
        var universe = ThreeGroups(6, 60, new Random(7));
        universe.RemoveAll(i => i.Symbol is "G20" or "G21" or "G22");
        var p = Params(("Basket14.Window", 40), ("Basket14.MinBars", 30), ("Basket14.Clusters", 3));

        var r = Eval(14, universe, Day(59), p);

        r.Excluded.Where(e => e.Reason == "small_cluster").Select(e => e.Symbol).ShouldBe(["G23", "G24", "G25"]);
        r.Members.Count.ShouldBe(12);
        (r.Members.Count + r.Excluded.Count).ShouldBe(universe.Count);
    }

    [Fact]
    public void A_corporate_action_unresolved_non_finite_or_zero_close_instrument_is_named()
    {
        var ok = Inst("OK", Flat(60, 100, 1));
        var ca = Inst("CA", Flat(60, 100, 1)) with { CorporateActionUnresolved = true };
        var nan = Flat(60, 100, 1);
        nan[30] = nan[30] with { High = double.NaN };
        var zero = Flat(60, 100, 1);
        zero[59] = zero[59] with { Close = 0 };

        var r = Eval(1, [ok, ca, Inst("NAN", nan), Inst("ZERO", zero)], Day(59));

        r.Excluded.ShouldBe([new("CA", "corporate_action_unresolved"), new("NAN", "bad_data"), new("ZERO", "bad_data")]);
        r.Members.Select(m => m.Symbol).ShouldBe(["OK"]);
    }

    [Fact]
    public void Ties_are_ordered_by_symbol_and_an_empty_or_single_universe_is_handled()
    {
        var b = Inst("B", Flat(60, 100, 1));
        var a = Inst("A", Flat(60, 100, 1));

        var tie = Eval(1, [b, a], Day(59), Params(("Basket1.TopK", 1)));
        var empty = Eval(1, [], Day(59));
        var one = Eval(1, [a], Day(59));

        tie.Members.Select(m => m.Symbol).ShouldBe(["A"]);
        empty.Members.ShouldBeEmpty();
        one.Members.Select(m => m.Symbol).ShouldBe(["A"]);
    }

    [Fact]
    public void A_slow_basket_keeps_a_young_member_outside_the_buffer_and_counts_its_sessions()
    {
        var p = Params(("Basket10.TopK", 1), ("Basket10.AdtvTop", 5));
        var low = Inst("LOW", FromReturns(260, 100, 0.002, -0.002));
        var mid = Inst("MID", FromReturns(260, 100, 0.01, -0.01));
        var hi = Inst("HI", FromReturns(260, 100, 0.02, -0.02));
        var previous = new DerivedResult([new("HI", 2)], [], [], [], [], null, p.Hash);

        var young = Eval(10, [low, mid, hi], Day(259), p, previous);
        var old = Eval(10, [low, mid, hi], Day(259), p, new DerivedResult([new("HI", 4)], [], [], [], [], null, p.Hash));

        young.Members.Select(m => m.Symbol).ShouldBe(["HI"]); // buffer 1.5 x 1 = 1, rank 3, dwell 3 < 5: stays
        young.Members.Single().SessionsHeld.ShouldBe(3);
        old.Members.Select(m => m.Symbol).ShouldBe(["LOW"]); // 5 sessions passed: leaves
        old.Members.Single().SessionsHeld.ShouldBe(0);
    }

    [Fact]
    public void A_dwelling_member_that_now_fails_the_hard_filter_leaves_with_its_reason_named()
    {
        var p = Params(("Basket10.TopK", 1), ("Basket10.AdtvTop", 5));
        var low = Inst("LOW", FromReturns(260, 100, 0.002, -0.002));
        var hi = Inst("HI", FromReturns(260, 100, 0.02, -0.02)) with { CorporateActionUnresolved = true };
        var previous = new DerivedResult([new("HI", 0)], [], [], [], [], null, p.Hash);

        var r = Eval(10, [low, hi], Day(259), p, previous);

        r.Members.Select(m => m.Symbol).ShouldBe(["LOW"]);
        r.Excluded.ShouldBe([new DerivedExclusion("HI", "corporate_action_unresolved")]);
    }
}
