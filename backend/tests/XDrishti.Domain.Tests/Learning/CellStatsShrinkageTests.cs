using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-02 acceptance criteria 6 to 11 on the calculator: the chain, the leave-one-out parent, the zero-information cases, beside-not-instead, idempotence, no leak and order independence.</summary>
public sealed class CellStatsShrinkageTests
{
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 180, PriorStrength: 20, MinTrades: 30);

    private static readonly ShrinkageOptions On = new();

    private static CellId Id(string strategy, string tf = "15m", string regime = "*") => new(strategy, tf, "BUY", "intraday", "FIXED_RR", regime);

    // n trades with the given mean and a spread of sd around it, all exited the day before the as-of date (so the recency weights are equal and the weighted mean is the plain one)
    private static List<CellTrade> Trades(int n, double mean, double sd = 1.0, int daysBefore = 1)
    {
        var exit = AsOf.AddDays(-daysBefore);
        return [.. Enumerable.Range(0, n).Select(k => new CellTrade(exit, n % 2 == 1 && k == n - 1 ? mean : mean + (k % 2 == 0 ? sd : -sd), 0.5, 1))];
    }

    private static CellInput Cell(CellId id, int n, double mean, double sd = 1.0, int daysBefore = 1) => new(id, true, Trades(n, mean, sd, daysBefore));

    private static CellStatsRow Pick(IReadOnlyList<CellStatsRow> rows, CellId id, CellWindow window = CellWindow.All) => rows.Single(r => r.Cell == id && r.Window == window);

    private static IReadOnlyList<CellStatsRow> Compute(IReadOnlyList<CellInput> cells, ShrinkageOptions? shrinkage = null, IReadOnlyList<CellInput>? regime = null) =>
        CellStatsCalculator.Compute(cells, AsOf, Options, regime, null, shrinkage ?? On);

    [Fact]
    public void Without_the_options_or_with_Off_the_rows_are_what_they_were_and_ReportOnly_changes_only_the_new_member()
    {
        var cells = new[] { Cell(Id("A"), 40, 0.2), Cell(Id("A", "5m"), 12, -0.3), Cell(Id("B"), 35, 0.1), Cell(Id("B", "5m"), 8, 0.5) };
        var plain = CellStatsCalculator.Compute(cells, AsOf, Options);

        var off = CellStatsCalculator.Compute(cells, AsOf, Options, null, null, new ShrinkageOptions(Mode: ConfidenceMode.Off));
        var report = Compute(cells);

        plain.ShouldAllBe(r => r.Shrinkage == null);
        off.ShouldBe(plain);
        report.Count.ShouldBe(plain.Count);
        report.ShouldAllBe(r => r.Shrinkage != null);
        for (var i = 0; i < plain.Count; i++)
        {
            (report[i] with { Shrinkage = null }).ShouldBe(plain[i]);   // Enough, N, the expectancy, the profit factor, the Beta posterior and Lower: the same
        }
    }

    [Fact]
    public void With_the_confidence_options_too_only_the_shrinkage_member_is_added()
    {
        var cells = new[] { Cell(Id("A"), 40, 0.2), Cell(Id("A", "5m"), 12, -0.3), Cell(Id("B"), 35, 0.1) };
        var with = CellStatsCalculator.Compute(cells, AsOf, Options, null, new ConfidenceOptions());

        var both = CellStatsCalculator.Compute(cells, AsOf, Options, null, new ConfidenceOptions(), On);

        for (var i = 0; i < with.Count; i++)
        {
            (both[i] with { Shrinkage = null }).ShouldBe(with[i]);
            both[i].Shrinkage.ShouldNotBeNull();
        }
    }

    [Fact]
    public void A_cell_is_shrunk_toward_the_other_cells_of_its_strategy_not_toward_itself()
    {
        // AC 6: A 10 trades at +0.5, B 90 at 0.0; the minimum for a parent lowered to 10 so A can be B's parent
        var a = Id("S", "5m");
        var b = Id("S", "15m");
        var rows = Compute([Cell(a, 10, 0.5), Cell(b, 90, 0.0)], new ShrinkageOptions(MinParentTrades: 10));

        var ra = Pick(rows, a).Shrinkage!;
        var rb = Pick(rows, b).Shrinkage!;

        ra.ParentLevel.ShouldBe("strategy");
        ra.ParentMeanR!.Value.ShouldBe(0.0, 1e-9);
        rb.ParentMeanR!.Value.ShouldBe(0.5, 1e-9);
        Pick(rows, a).PriorRate.ShouldBe(Pick(rows, b).PriorRate);   // the P6-01 column still has one parent for both
        ra.ShrunkMeanR!.Value.ShouldBeInRange(0.0, 0.5);
        ra.Blend!.Value.ShouldBeInRange(0, 1);
    }

    [Fact]
    public void A_cell_that_is_most_of_its_group_has_no_parent_with_enough_trades_and_says_so()
    {
        var a = Id("S", "5m");
        var b = Id("S", "15m");
        var rows = Compute([Cell(a, 10, 0.5), Cell(b, 90, 0.0)]);

        var rb = Pick(rows, b).Shrinkage!;

        Pick(rows, a).Shrinkage!.IsAvailable.ShouldBeTrue();   // B has 90 trades
        rb.IsAvailable.ShouldBeFalse();
        rb.Note.ShouldBe("unavailable: no parent with 30 trades");
        rb.ParentMeanR.ShouldBeNull();
        rb.ShrunkMeanR.ShouldBeNull();
        rb.ShrunkLowerR.ShouldBeNull();
        rb.ShrunkWinRate.ShouldBeNull();
        Pick(rows, b).ExpectancyR.ShouldNotBeNull();   // the raw figures are shown as they were
    }

    [Fact]
    public void A_regime_cell_is_shrunk_toward_its_star_cell_without_itself_when_that_has_30_trades()
    {
        var star = Id("S");
        var regime = Id("S", regime: "up");
        // the star cell has 70 trades, 12 of them in the regime cell: 58 are left
        var all = Trades(58, 0.1).Concat(Trades(12, 0.8)).ToList();
        var rows = Compute([new CellInput(star, true, all)], null, [new CellInput(regime, true, Trades(12, 0.8))]);

        var r = Pick(rows, regime).Shrinkage!;

        r.ParentLevel.ShouldBe("star");
        r.ParentMeanR!.Value.ShouldBe(0.1, 1e-9);
        r.ShrunkMeanR!.Value.ShouldBeInRange(0.1, 0.8);
    }

    [Fact]
    public void The_chain_falls_through_to_strategy_then_system_then_unavailable()
    {
        var star = Id("S");
        var regime = Id("S", regime: "up");
        var regimeCell = new CellInput(regime, true, Trades(12, 0.8));
        var starCell = new CellInput(star, true, Trades(28, 0.1).Concat(Trades(12, 0.8)).ToList());   // 40 trades, 28 without the regime cell: under 30

        var strategy = Compute([starCell, Cell(Id("S", "5m"), 35, 0.0)], null, [regimeCell]);
        var system = Compute([starCell, Cell(Id("S", "5m"), 20, 0.0), Cell(Id("T"), 45, -0.1)], null, [regimeCell]);
        var none = Compute([starCell, Cell(Id("S", "5m"), 20, 0.0), Cell(Id("T"), 5, -0.1)], null, [regimeCell]);

        Pick(strategy, regime).Shrinkage!.ParentLevel.ShouldBe("strategy");
        Pick(strategy, regime).Shrinkage!.ParentMeanR!.Value.ShouldBe(0.0, 1e-9);
        Pick(system, regime).Shrinkage!.ParentLevel.ShouldBe("system");
        Pick(none, regime).Shrinkage!.Note.ShouldBe("unavailable: no parent with 30 trades");
        Pick(none, regime).Shrinkage!.ParentLevel.ShouldBeNull();
        Pick(strategy, star).Shrinkage!.ParentLevel.ShouldBe("strategy");   // a star cell starts at its strategy
    }

    [Fact]
    public void Adding_regime_cells_does_not_change_a_star_cell()
    {
        var cells = new[] { Cell(Id("S"), 60, 0.2), Cell(Id("S", "5m"), 40, 0.0), Cell(Id("T"), 40, 0.1) };
        var split = new CellInput(Id("S", regime: "up"), true, Trades(10, 0.3));

        var without = Compute(cells);
        var with = Compute(cells, null, [split]);

        foreach (var c in cells)
        {
            Pick(with, c.Cell).Shrinkage.ShouldBe(Pick(without, c.Cell).Shrinkage);
        }
    }

    [Fact]
    public void A_group_with_fewer_than_three_siblings_uses_the_floor_and_the_default_strength_and_says_so()
    {
        var rows = Compute([Cell(Id("S"), 40, 0.4), Cell(Id("S", "5m"), 40, 0.0)]);

        var r = Pick(rows, Id("S")).Shrinkage!;

        r.TauSource.ShouldBe("floor");
        r.TauSquared.ShouldBe(0.01);
        r.StrengthSource.ShouldBe("default");
        r.EstimatedStrength.ShouldBe(20);
        r.Note.ShouldContain("fewer than 3 siblings");
    }

    [Fact]
    public void With_a_real_spread_between_five_cells_tau_squared_is_estimated()
    {
        var means = new[] { 1.0, -0.8, 0.9, -0.7, 0.0 };
        var cells = means.Select((m, i) => Cell(Id("S", "tf" + i), 40, m, 0.8)).ToList();

        var rows = Compute(cells);

        var r = Pick(rows, Id("S", "tf0")).Shrinkage!;
        r.TauSource.ShouldBe("estimated");
        r.TauSquared!.Value.ShouldBeGreaterThan(0.01);
        r.ShrunkMeanR!.Value.ShouldBeLessThan(1.0);
        r.ShrunkMeanR.Value.ShouldBeGreaterThan(r.ParentMeanR!.Value);
        r.NUsed!.Value.ShouldBe(40, 1e-9);   // equal weights: the Kish n is the count
        r.Note.ShouldContain("effective n unavailable");   // no CF-01 options: the weight n alone, said on the row
    }

    [Fact]
    public void The_smaller_of_effective_n_and_the_weight_n_is_used()
    {
        // 24 trades in 8 entry sessions whose levels alternate: strongly clustered, so effective n is well below the weight n
        var cells = Enumerable.Range(0, 5).Select(i => new CellInput(Id("S", "tf" + i), true, [.. Enumerable.Range(0, 24).Select(k => new CellTrade(AsOf.AddDays(-1 - (k / 3)), (i * 0.3) + (k / 3 % 2 == 0 ? 0.8 : -0.8) + (k % 2 * 0.1), 0.5, 1, AsOf.AddDays(-2 - (k / 3))))])).ToList();

        var rows = CellStatsCalculator.Compute(cells, AsOf, Options, null, new ConfidenceOptions(), On);

        var row = Pick(rows, Id("S", "tf2"));
        var nEff = row.Dependence!.NEff;
        nEff.ShouldNotBeNull();
        nEff!.Value.ShouldBeLessThan(23);
        row.Shrinkage!.NUsed!.Value.ShouldBe(nEff.Value, 1e-9);
        row.Shrinkage.Note.ShouldNotContain("effective n unavailable");
    }

    [Fact]
    public void A_cell_with_no_trade_in_a_window_reads_the_parent_with_no_blend()
    {
        // old trades only: empty in the 250d and 60d windows, so the other cells carry the 30 parent trades there
        var quiet = Id("S", "quiet");
        var cells = new[] { Cell(quiet, 12, 0.7, 1.0, 400), Cell(Id("S", "5m"), 40, 0.2, 1.0, 5), Cell(Id("S", "15m"), 40, 0.0, 1.0, 5) };

        var rows = Compute(cells);

        var r = Pick(rows, quiet, CellWindow.Days60);
        r.N.ShouldBe(0);
        r.Shrinkage!.ShrunkMeanR!.Value.ShouldBe(r.Shrinkage.ParentMeanR!.Value, 1e-12);
        r.Shrinkage.Blend.ShouldBe(0);
        r.Shrinkage.NUsed.ShouldBe(0);
        r.Shrinkage.ShrunkLowerR!.Value.ShouldBeLessThan(r.Shrinkage.ShrunkMeanR.Value);   // wide: the spread is tau
    }

    [Fact]
    public void No_spread_in_R_is_floored_not_divided_by()
    {
        var flat = Id("S", "flat");
        var rows = Compute([Cell(flat, 10, 0.3, 0.0), Cell(Id("S", "5m"), 40, 0.0), Cell(Id("S", "15m"), 40, 0.1)]);

        var r = Pick(rows, flat).Shrinkage!;

        r.IsAvailable.ShouldBeTrue();
        double.IsFinite(r.ShrunkMeanR!.Value).ShouldBeTrue();
        double.IsFinite(r.ShrunkLowerR!.Value).ShouldBeTrue();
    }

    [Fact]
    public void A_result_that_is_not_a_number_is_unavailable_never_a_number()
    {
        var bad = Id("S", "bad");
        var trades = Trades(10, 0.3);
        trades[3] = trades[3] with { R = double.NaN };

        var rows = Compute([new CellInput(bad, true, trades), Cell(Id("S", "5m"), 40, 0.0)]);

        var r = Pick(rows, bad).Shrinkage!;
        r.IsAvailable.ShouldBeFalse();
        r.Note.ShouldBe("unavailable: a result is not a finite number");
        r.ShrunkWinRate.ShouldBeNull();
    }

    [Fact]
    public void A_cell_with_few_trades_pools_its_variance_with_the_group_and_says_so()
    {
        var thin = Id("S", "thin");
        var rows = Compute([Cell(thin, 2, 0.5), Cell(Id("S", "5m"), 40, 0.0), Cell(Id("S", "15m"), 40, 0.1)]);

        var r = Pick(rows, thin).Shrinkage!;

        r.IsAvailable.ShouldBeTrue();
        r.Note.ShouldContain("variance of R taken from the group");
        r.Blend!.Value.ShouldBeLessThan(0.1);
    }

    [Fact]
    public void The_shrunk_win_rate_uses_the_leave_one_out_rate_and_the_strength()
    {
        var a = Id("S", "5m");
        var b = Id("S", "15m");
        // A: 10 trades, 6 wins; B: 40 trades, 10 wins -> A's parent win rate is B's 0.25; strength is the default 20 (two cells)
        var winsA = Enumerable.Range(0, 10).Select(k => new CellTrade(AsOf.AddDays(-1), k < 6 ? 1 : -1, 0.5, 1)).ToList();
        var winsB = Enumerable.Range(0, 40).Select(k => new CellTrade(AsOf.AddDays(-1), k < 10 ? 1 : -1, 0.5, 1)).ToList();

        var rows = Compute([new CellInput(a, true, winsA), new CellInput(b, true, winsB)]);

        var r = Pick(rows, a).Shrinkage!;
        var w = Math.Pow(0.5, 1.0 / 180);   // one recency weight, equal for every trade
        r.ShrunkWinRate!.Value.ShouldBe(((20 * 0.25) + (6 * w)) / (20 + (10 * w)), 1e-9);
        r.StrengthSource.ShouldBe("default");
        Pick(rows, a).WinRatePosterior.ShouldNotBe(r.ShrunkWinRate.Value);   // the strength-20 posterior with the old parent stays on the row
    }

    [Fact]
    public void Recomputing_gives_the_same_numbers_and_a_later_trade_changes_nothing()
    {
        var cells = new List<CellInput> { Cell(Id("S"), 40, 0.2), Cell(Id("S", "5m"), 30, -0.1), Cell(Id("S", "15m"), 35, 0.3), Cell(Id("T"), 50, 0.0) };
        var first = Compute(cells);
        var later = cells[0] with { Trades = [.. cells[0].Trades, new CellTrade(AsOf.AddDays(1), 9, 0.5, 1)] };

        var again = Compute(cells);
        var withLeak = Compute([later, .. cells.Skip(1)]);

        again.ShouldBe(first);
        withLeak.ShouldBe(first);
    }

    [Fact]
    public void Shuffling_the_cells_leaves_each_cells_figures_unchanged()
    {
        var cells = new List<CellInput>();
        var rng = new Random(3);
        foreach (var strategy in new[] { "S", "T" })
        {
            for (var i = 0; i < 6; i++)
            {
                cells.Add(Cell(Id(strategy, "tf" + i), 8 + rng.Next(40), (rng.NextDouble() - 0.5) * 1.5, 0.5 + rng.NextDouble()));
            }
        }

        var regime = new[] { new CellInput(Id("S", "tf0", "up"), true, Trades(6, 0.4)), new CellInput(Id("T", "tf1", "down"), true, Trades(9, -0.2)) };
        var baseline = Compute(cells, null, regime);
        for (var seed = 1; seed <= 5; seed++)
        {
            var shuffler = new Random(seed);
            var shuffled = cells.OrderBy(_ => shuffler.Next()).ToList();
            var rows = Compute(shuffled, null, regime.Reverse().ToArray());
            foreach (var row in baseline)
            {
                rows.Single(r => r.Cell == row.Cell && r.Window == row.Window).Shrinkage.ShouldBe(row.Shrinkage);
            }
        }
    }

    [Fact]
    public void Gate_is_not_offered_and_the_calculator_refuses_it()
    {
        var gate = new ShrinkageOptions(Mode: ConfidenceMode.Gate);

        gate.Problems().ShouldContain(p => p.Contains("Gate is not offered", StringComparison.Ordinal));
        Should.Throw<ArgumentException>(() => CellStatsCalculator.Compute([Cell(Id("S"), 40, 0.2)], AsOf, Options, null, null, gate));
        new ShrinkageOptions(Mode: ConfidenceMode.Off).Problems().ShouldBeEmpty();
        On.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void The_floor_must_be_above_0_and_at_most_1(double floor) => new ShrinkageOptions(TauSquaredFloor: floor).Problems().ShouldNotBeEmpty();

    [Fact]
    public void Every_setting_is_range_checked()
    {
        new ShrinkageOptions(MinTradesForSibling: 2).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(MinTradesForSibling: 31).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(MinParentTrades: 9).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(MinParentTrades: 201).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(StrengthMin: 0.5).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(StrengthMax: 1001).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(StrengthMin: 50, StrengthMax: 10).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(QuantileLevel: 0.005).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(QuantileLevel: 0.3).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(SigmaPoolStrength: 0).Problems().ShouldNotBeEmpty();
        new ShrinkageOptions(Mode: (ConfidenceMode)9).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_key_text_changes_with_every_value()
    {
        var keys = new[] { On, On with { TauSquaredFloor = 0.02 }, On with { MinTradesForSibling = 6 }, On with { MinParentTrades = 31 }, On with { StrengthMin = 6 }, On with { StrengthMax = 201 }, On with { QuantileLevel = 0.06 }, On with { SigmaPoolStrength = 31 } }
            .Select(o => o.KeyText()).ToList();

        keys.Distinct().Count().ShouldBe(keys.Count);
    }
}
