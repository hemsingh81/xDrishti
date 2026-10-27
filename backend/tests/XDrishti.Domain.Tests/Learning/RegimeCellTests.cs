using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>P6-02 (ADR 0146): regime cells split a cell by the regime of its trades, never mix the two bases, count what has no label, and leave the rows of the <c>*</c> cells as they were.</summary>
public sealed class RegimeCellTests
{
    private static readonly DateOnly AsOf = new(2026, 3, 10);

    private static readonly CellId Orb = new("ORB", "15m", "BUY", "intraday", "FIXED_RR");

    private static readonly CellId Vwap = new("VWAP_PULLBACK", "15m", "BUY", "intraday", "FIXED_RR");

    private static CellTrade Trade(int daysAgo, double r) => new(AsOf.AddDays(-daysAgo), r, 0.5, 1.5);

    private static RegimeLabel Label(int? code, string basis = ContextBasis.IndexAndVix, string? reason = null) =>
        code is null
            ? new RegimeLabel(AsOf, null, null, null, null, reason ?? ContextRead.BeforeHistory)
            : new RegimeLabel(AsOf, code, RegimeNames.NameOf(code.Value), basis, AsOf.AddDays(-1), null);

    private static CellInput Cell(CellId id, params CellTrade[] trades) => new(id, false, trades);

    [Fact]
    public void Trades_go_to_the_cell_of_their_regime_part_and_the_two_bases_are_two_cells()
    {
        var cell = Cell(Orb, Trade(1, 1), Trade(2, -1), Trade(3, 2), Trade(4, 3), Trade(5, 1));
        IReadOnlyList<RegimeLabel> labels = [Label(1), Label(1, ContextBasis.IndexOnly), Label(1), Label(null), Label(2)];

        var built = RegimeCellBuilder.Build([cell], [labels]);

        built.Cells.Select(c => c.Cell.Key).ShouldBe(
        [
            "ORB|15m|BUY|intraday|FIXED_RR|UP_LOW_VOL",
            "ORB|15m|BUY|intraday|FIXED_RR|UP_LOW_VOL~index_only",
            "ORB|15m|BUY|intraday|FIXED_RR|UP_NORMAL_VOL",
        ]);
        built.Cells.Select(c => c.Trades.Count).ShouldBe([2, 1, 1]);
        built.Cells.ShouldAllBe(c => c.Costed == cell.Costed);
        (built.Tally.Labelled, built.Tally.IndexAndVix, built.Tally.IndexOnly, built.Tally.Unavailable).ShouldBe((4, 3, 1, 1));
    }

    [Fact]
    public void The_trades_of_the_regime_cells_and_the_unlabelled_ones_are_exactly_the_trades_of_the_cell()
    {
        var random = new Random(7);
        var trades = Enumerable.Range(1, 200).Select(i => Trade(i, random.NextDouble() * 4 - 2)).ToArray();
        var labels = trades.Select(_ => random.Next(0, 12) switch { 0 => Label(null), 1 => Label(null, reason: ContextRead.Stale), < 5 => Label(random.Next(1, 10)), _ => Label(random.Next(1, 10), ContextBasis.IndexOnly) }).ToList();

        var built = RegimeCellBuilder.Build([Cell(Orb, trades)], [labels]);

        (built.Cells.Sum(c => c.Trades.Count) + built.Tally.Unavailable).ShouldBe(trades.Length);
        built.Cells.SelectMany(c => c.Trades).Sum(t => t.R).ShouldBe(trades.Zip(labels).Where(p => p.Second.IsAvailable).Sum(p => p.First.R), 1e-9);
        built.Cells.Select(c => c.Cell.Key).ShouldBeUnique();
    }

    [Fact]
    public void A_cell_with_no_label_at_all_has_no_regime_cell_and_every_trade_is_counted()
    {
        var built = RegimeCellBuilder.Build([Cell(Orb, Trade(1, 1), Trade(2, 1))], [[Label(null), Label(null, reason: ContextRead.NoContext)]]);

        built.Cells.ShouldBeEmpty();
        built.Tally.UnavailableByReason.ShouldBe(new Dictionary<string, int> { ["before_history"] = 1, ["no_context"] = 1 });
    }

    [Fact]
    public void A_list_of_labels_that_does_not_match_the_trades_is_refused()
    {
        Should.Throw<ArgumentException>(() => RegimeCellBuilder.Build([Cell(Orb, Trade(1, 1))], [[]]));
        Should.Throw<ArgumentException>(() => RegimeCellBuilder.Build([Cell(Orb, Trade(1, 1))], []));
    }

    [Fact]
    public void The_star_rows_are_identical_with_regime_cells_and_the_regime_rows_are_shrunk_toward_their_strategy_rate()
    {
        var options = new CellStatsOptions(RecencyHalfLifeDays: 30, PriorStrength: 5, MinTrades: 2);
        var orb = Cell(Orb, Trade(1, 1), Trade(2, -1), Trade(3, 2), Trade(4, 1));
        var vwap = Cell(Vwap, Trade(1, -1), Trade(2, -1));
        var built = RegimeCellBuilder.Build([orb, vwap], [[Label(1), Label(1), Label(2), Label(null)], [Label(1), Label(1)]]);

        var plain = CellStatsCalculator.Compute([orb, vwap], AsOf, options);
        var withRegime = CellStatsCalculator.Compute([orb, vwap], AsOf, options, built.Cells);

        withRegime.Take(plain.Count).ShouldBe(plain);
        var regimeRows = withRegime.Skip(plain.Count).ToList();
        regimeRows.Count.ShouldBe(built.Cells.Count * 3);
        regimeRows.Select(r => r.Cell.Regime).Distinct().Order(StringComparer.Ordinal).ShouldBe(["UP_LOW_VOL", "UP_NORMAL_VOL"]);
        var orbStarAll = plain.Single(r => r.Cell.Key.StartsWith("ORB", StringComparison.Ordinal) && r.Window == CellWindow.All);
        var orbUpLow = regimeRows.Single(r => r.Cell.Strategy == "ORB" && r.Cell.Regime == "UP_LOW_VOL" && r.Window == CellWindow.All);
        orbUpLow.PriorRate.ShouldBe(orbStarAll.PriorRate, "the prior is the strategy's rate, taken from the star cells only");
        orbUpLow.N.ShouldBe(2);
        orbUpLow.Enough.ShouldBeTrue();
        regimeRows.Single(r => r.Cell.Strategy == "ORB" && r.Cell.Regime == "UP_NORMAL_VOL" && r.Window == CellWindow.All).Enough.ShouldBeFalse();
    }

    [Fact]
    public void A_regime_cell_of_a_strategy_that_has_no_star_cell_is_refused()
    {
        var stray = Cell(Vwap with { Regime = "UP_LOW_VOL" }, Trade(1, 1));

        Should.Throw<ArgumentException>(() => CellStatsCalculator.Compute([Cell(Orb, Trade(1, 1))], AsOf, new CellStatsOptions(), [stray]));
    }

    [Fact]
    public void The_breakdown_splits_the_trades_by_part_flags_thin_buckets_and_says_what_has_no_regime()
    {
        var thin = Enumerable.Range(0, 4).Select(_ => (Label(1), 0.5)).ToList();
        var thick = Enumerable.Range(0, 30).Select(i => (Label(2), i % 2 == 0 ? 1.0 : -0.5)).ToList();
        var onlyIndex = new[] { (Label(1, ContextBasis.IndexOnly), 2.0) };
        var none = new[] { (Label(null, reason: ContextRead.BeforeHistory), 1.0) };

        var result = RegimeBreakdown.Of([.. thin, .. thick, .. onlyIndex, .. none]);

        result.Buckets.Select(b => (b.Part, b.Trades, b.Enough)).ShouldBe([("UP_LOW_VOL", 4, false), ("UP_LOW_VOL~index_only", 1, false), ("UP_NORMAL_VOL", 30, true)]);
        result.Buckets[0].MeanR.ShouldBe(0.5);
        result.Buckets[2].TotalR.ShouldBe(7.5, 1e-9);
        result.Tally.UnavailableByReason.ShouldBe(new Dictionary<string, int> { ["before_history"] = 1 });
        result.Text.ShouldContain("UP_LOW_VOL 4 trades, mean 0.50 R (not enough evidence)");
        result.Text.ShouldContain("unavailable before_history 1");
    }

    [Fact]
    public void The_breakdown_of_trades_with_no_regime_is_unavailable_with_the_reasons_and_of_no_trades_says_so()
    {
        RegimeBreakdown.Of([(Label(null, reason: ContextRead.Stale), 1.0)]).Text.ShouldBe("unavailable: no out-of-sample trade has a regime (unavailable stale 1)");
        RegimeBreakdown.Of([]).Text.ShouldBe("unavailable: there are no out-of-sample trades");
        RegimeBreakdown.Of([(Label(null), 1.0)]).Buckets.ShouldBeEmpty();
    }
}
