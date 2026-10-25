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

    [Fact]
    public void An_unclassified_sector_is_not_recorded_not_a_value()
    {
        var trades = Enumerable.Range(0, 3).Select(i => Ex($"k{i}", 1, 0, 0, i, null, "Banks")).Concat([Ex("n", 1, 0, 0, 5)]).ToList();

        var groups = Group(Population([Run("r1")], trades), ReportDimension.Sector).Groups;

        groups.Single(g => g.Key.ValueOf(ReportDimension.Sector) == "Banks").N.ShouldBe(3);
        groups[^1].Key.ValueOf(ReportDimension.Sector).ShouldBeNull();
    }
}
