using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>P8-10: the excursion counts, the entry hour and the sector dimension against hand tables.</summary>
public sealed class ReportMaeMfeTests
{
    private static ReportTrade Ex(string key, double r, double? mae, double? mfe, int day = 0, int? minute = null, string? sector = null, string run = "r1") =>
        Trade(run, key, r, day: day) with { MaeR = mae, MfeR = mfe, EntryMinuteIst = minute, Sector = sector };

    [Fact]
    public void Levels_reached_count_the_trades_at_or_above_each_level()
    {
        double[] mfe = [0.5, 1.0, 1.2, 1.9, 2.0, 2.5, 3.0, 3.1, 4.0, 0.0];
        var trades = mfe.Select((m, i) => Ex($"k{i}", 1, 0.1, m, i)).ToList();

        var group = Population([Run("r1")], trades).MaeMfe([]).Value.Groups.Single();

        group.ExcursionN.ShouldBe(10);
        group.ReachedMfe.Select(l => (l.AtR, l.Count, l.Share)).ShouldBe([(1.0, 8, 0.8), (2.0, 5, 0.5), (3.0, 3, 0.3)]);
        group.Mfe!.Counts.Sum().ShouldBe(10);
    }

    [Fact]
    public void The_winners_adverse_p90_is_the_nearest_rank_value()
    {
        var trades = Enumerable.Range(1, 10).Select(i => Ex($"k{i}", 1, i / 10.0, 1, i)).ToList();

        var group = Population([Run("r1")], trades).MaeMfe([]).Value.Groups.Single();

        group.WinnersMaeP90.Number!.Value.ShouldBe(0.9, 1e-12);
    }

    [Fact]
    public void Too_few_winners_give_a_reason_and_no_number()
    {
        var trades = new List<ReportTrade> { Ex("a", 1, 0.2, 1), Ex("b", -1, 0.9, 0.1, 1), Ex("c", -1, 0.9, 0.1, 2) };
        var options = Small(3) with { MinWins = 2 };

        var group = Population([Run("r1")], trades, options).MaeMfe([]).Value.Groups.Single();

        group.WinnersMaeP90.State.ShouldBe(MetricState.Insufficient);
        group.WinnersMaeP90.Number.ShouldBeNull();
    }

    [Fact]
    public void Grid_counts_sum_to_the_trades_with_an_excursion_and_the_tails_hold_the_extremes()
    {
        var trades = new List<ReportTrade> { Ex("a", -7, 3, 0, 0), Ex("b", 6, 0.1, 6, 1), Ex("c", 0.2, 0.3, 1, 2), Ex("d", 0.2, null, null, 3) };

        var group = Population([Run("r1")], trades).MaeMfe([]).Value.Groups.Single();

        group.N.ShouldBe(4);
        group.ExcursionN.ShouldBe(3);
        group.Grid!.Counts.Sum(r => r.Sum()).ShouldBe(3);
        group.Grid.BelowCount.ShouldBe(1);
        group.Grid.AboveCount.ShouldBe(1);
        group.Grid.Counts[^1][0].ShouldBe(1);
        group.Mfe!.AboveCount.ShouldBe(1);
    }

    [Fact]
    public void A_group_with_no_recorded_excursion_has_no_counts_and_no_share()
    {
        var group = One([Ex("a", 1, null, null), Ex("b", -1, null, null, 1), Ex("c", 1, null, null, 2)]);

        group.ExcursionN.ShouldBe(0);
        group.Grid.ShouldBeNull();
        group.ReachedMfe.ShouldBeEmpty();
        group.WinnersMaeP90.State.ShouldBe(MetricState.Unavailable);
    }

    [Fact]
    public void A_thin_group_has_only_the_reason()
    {
        var group = Population([Run("r1")], [Ex("a", 1, 0.1, 1), Ex("b", 1, 0.1, 1, 1)]).MaeMfe([]).Value.Groups.Single();

        group.Grid.ShouldBeNull();
        group.WinnersMaeP90.State.ShouldBe(MetricState.Insufficient);
    }

    [Fact]
    public void Hour_is_the_india_hour_of_an_intraday_entry_and_a_swing_run_has_none()
    {
        var trades = new List<ReportTrade>
        {
            Ex("a", 1, 0, 0, 0, (9 * 60) + 20),
            Ex("b", 1, 0, 0, 1, (9 * 60) + 59),
            Ex("c", 1, 0, 0, 2, 14 * 60),
            Ex("s", 1, 0, 0, 3, (9 * 60) + 20, run: "r2"),
        };

        var groups = Group(Population([Run("r1"), Run("r2", style: "swing")], trades, Small(1)), ReportDimension.Hour, ReportDimension.Style).Groups;

        groups.Select(g => (g.Key.Text, g.N)).ShouldContain(("Hour=09|Style=intraday", 2));
        groups.Select(g => (g.Key.Text, g.N)).ShouldContain(("Hour=14|Style=intraday", 1));
        groups.Single(g => g.Key.ValueOf(ReportDimension.Style) == "swing").Key.ValueOf(ReportDimension.Hour).ShouldBeNull();
    }

    private static ReportMaeMfeGroup One(IEnumerable<ReportTrade> trades) =>
        Population([Run("r1")], [.. trades], Small(1)).MaeMfe([]).Value.Groups.Single();

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.24, 0)]
    [InlineData(0.25, 1)]
    [InlineData(1.99, 7)]
    [InlineData(2.0, 8)]
    [InlineData(9.0, 8)]
    public void A_MAE_on_a_bin_edge_belongs_to_the_bin_that_starts_there(double mae, int row)
    {
        var grid = One([Ex("a", 0.3, mae, 0)]).Grid!;

        grid.Counts[row].Sum().ShouldBe(1);
        grid.Counts.Sum(r => r.Sum()).ShouldBe(1);
    }

    [Theory]
    [InlineData(-9.0, 0)]
    [InlineData(-5.01, 0)]
    [InlineData(-5.0, 1)]
    [InlineData(-0.5, 10)]
    [InlineData(0.0, 11)]
    [InlineData(0.49, 11)]
    [InlineData(0.5, 12)]
    [InlineData(4.99, 20)]
    [InlineData(5.0, 21)]
    public void A_result_on_a_bin_edge_belongs_to_the_column_that_starts_there(double result, int column)
    {
        var grid = One([Ex("a", result, 0.3, 0)]).Grid!;

        grid.Counts[1][column].ShouldBe(1);
        grid.Counts.Sum(r => r.Sum()).ShouldBe(1);
        grid.BelowCount.ShouldBe(column == 0 ? 1 : 0);
        grid.AboveCount.ShouldBe(column == 21 ? 1 : 0);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(4.99, 9)]
    public void An_MFE_on_a_bin_edge_belongs_to_the_bin_that_starts_there(double mfe, int bin)
    {
        var histogram = One([Ex("a", 0.3, 0.1, mfe)]).Mfe!;

        histogram.Counts[bin].ShouldBe(1);
        histogram.Counts.Sum().ShouldBe(1);
        histogram.AboveCount.ShouldBe(0);
    }

    [Fact]
    public void An_MFE_of_five_r_is_in_the_open_bin()
    {
        var histogram = One([Ex("a", 0.3, 0.1, 5.0)]).Mfe!;

        histogram.AboveCount.ShouldBe(1);
        histogram.Counts.Sum().ShouldBe(0);
    }

    [Theory]
    [InlineData(9 * 60 + 59, "09")]
    [InlineData(10 * 60, "10")]
    [InlineData(15 * 60 + 29, "15")]
    public void The_hour_changes_on_the_hour(int minute, string hour)
    {
        var groups = Group(Population([Run("r1")], [Ex("a", 1, 0, 0, 0, minute)], Small(1)), ReportDimension.Hour).Groups;

        groups.Single().Key.ValueOf(ReportDimension.Hour).ShouldBe(hour);
    }

    [Fact]
    public void The_distribution_of_the_spec_hand_table_has_the_hand_counted_bins()
    {
        double[] rs = [-1.0, -0.7, -0.2, 0.0, 0.3, 0.5, 1.9, 2.0, 6.0, -7.0];
        var population = Population([Run("r1")], [.. rs.Select((r, i) => Trade("r1", $"k{i}", r, day: i))], Small(1));

        var bins = population.Distribution([], 0.5).Value.Groups.Single().Bins;

        int Count(double? lower) => bins.Single(b => b.LowerR == lower).Count;
        Count(null).ShouldBe(1);
        Count(-1.0).ShouldBe(2);
        Count(-0.5).ShouldBe(1);
        Count(0.0).ShouldBe(2);
        Count(0.5).ShouldBe(1);
        Count(1.5).ShouldBe(1);
        Count(2.0).ShouldBe(1);
        Count(6.0).ShouldBe(1);
        bins.Sum(b => b.Count).ShouldBe(10);
    }

    [Fact]
    public void The_order_of_the_trades_does_not_change_the_counts_and_one_group_does_not_change_another()
    {
        var trades = Enumerable.Range(0, 24).Select(i => Ex($"k{i}", (i % 7) - 3.0, (i % 5) * 0.3, (i % 9) * 0.7, i) with { Side = i % 2 == 0 ? "BUY" : "SELL" }).ToList();
        var shuffled = trades.OrderBy(t => (t.TradeKey.GetHashCode(StringComparison.Ordinal) * 31) % 17).ThenBy(t => t.TradeKey, StringComparer.Ordinal).Reverse().ToList();

        var one = Population([Run("r1")], trades, Small(1)).MaeMfe([ReportDimension.Side]).Value.Groups;
        var two = Population([Run("r1")], shuffled, Small(1)).MaeMfe([ReportDimension.Side]).Value.Groups;
        var buyOnly = Population([Run("r1")], [.. trades.Where(t => t.Side == "BUY")], Small(1)).MaeMfe([ReportDimension.Side]).Value.Groups;

        Signature(two).ShouldBe(Signature(one));
        Signature(one.Where(g => g.Key.Text == "Side=BUY")).ShouldBe(Signature(buyOnly));
    }

    private static string Signature(IEnumerable<ReportMaeMfeGroup> groups) =>
        string.Join(';', groups.Select(g => $"{g.Key.Text}:{g.N}:{string.Join(',', g.Grid!.Counts.SelectMany(r => r))}:{string.Join(',', g.Mfe!.Counts)}:{g.Mfe.AboveCount}"));

    [Fact]
    public void An_unclassified_sector_is_not_recorded_not_a_value()
    {
        var trades = Enumerable.Range(0, 3).Select(i => Ex($"k{i}", 1, 0, 0, i, null, "Banks")).Concat([Ex("n", 1, 0, 0, 5)]).ToList();

        var groups = Group(Population([Run("r1")], trades), ReportDimension.Sector).Groups;

        groups.Single(g => g.Key.ValueOf(ReportDimension.Sector) == "Banks").N.ShouldBe(3);
        groups[^1].Key.ValueOf(ReportDimension.Sector).ShouldBeNull();
    }
}
