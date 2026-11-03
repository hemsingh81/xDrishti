using XDrishti.Domain.Lab;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13 criterion 4 (the formation half): the window of a quarter looks at daily points dated before the quarter only; changing or adding anything on or after the quarter start changes neither the members nor the formation id.</summary>
public sealed class PairWindowTests
{
    private static readonly DateOnly Quarter = new(2025, 7, 1);
    private static readonly PairStudyOptions O = PairStudyOptions.Default;

    private static List<DateOnly> Days(DateOnly last, int count)
    {
        var days = new List<DateOnly>();
        for (var d = last; days.Count < count; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(d);
            }
        }

        days.Reverse();
        return days;
    }

    private static DailySeries Series(string symbol, ulong seed, int count = 300, string? sector = "Banks")
    {
        var walk = PairsData.Walk(new SeededRandom(seed), count);
        return new DailySeries(symbol, sector, [.. Days(new DateOnly(2025, 6, 30), count).Select((d, i) => new DailyPoint(d, Math.Exp(4.6 + walk[i]), 1000 * (i + 1)))]);
    }

    private static string Id(PairWindowResult w) => PairFormationId.Of("2025Q3", w.Sessions[0], w.Sessions[^1], O, w.Members);

    [Fact]
    public void The_window_is_the_last_252_sessions_before_the_quarter_in_symbol_order_with_the_mean_turnover_of_the_last_20()
    {
        var w = PairWindow.Build([Series("NSE:B", 2), Series("NSE:A", 1)], Quarter, O);

        w.Sessions.Count.ShouldBe(252);
        w.Sessions[^1].ShouldBe(new DateOnly(2025, 6, 30));
        w.Members.Select(m => m.Symbol).ShouldBe(["NSE:A", "NSE:B"]);
        w.Members[0].Closes.Count.ShouldBe(252);
        w.Members[0].MeanTurnover.ShouldBe(Enumerable.Range(281, 20).Average(i => 1000.0 * i), 1e-9);
        w.TooFewSessions.ShouldBeFalse();
    }

    [Fact]
    public void Points_on_or_after_the_quarter_start_and_an_instrument_that_starts_then_change_nothing()
    {
        var a = Series("NSE:A", 1);
        var b = Series("NSE:B", 2);
        var before = PairWindow.Build([a, b], Quarter, O);
        var late = new DailySeries("NSE:LATE", "Banks", [.. Days(new DateOnly(2025, 9, 30), 30).Select(d => new DailyPoint(d, 100, 1))]);
        var moved = a with { Points = [.. a.Points, new DailyPoint(Quarter, 1, 1), new DailyPoint(Quarter.AddDays(30), 9999, 9999)] };

        var after = PairWindow.Build([moved, b, late], Quarter, O);

        after.Members.Select(m => m.Symbol).ShouldBe(before.Members.Select(m => m.Symbol));
        Id(after).ShouldBe(Id(before));
        after.Covered.ShouldBe(2);
    }

    [Fact]
    public void Changing_a_close_inside_the_window_changes_the_formation_id_the_control()
    {
        var a = Series("NSE:A", 1);
        var before = PairWindow.Build([a], Quarter, O);
        var changed = a with { Points = [.. a.Points.Select((p, i) => i == 100 ? p with { Close = p.Close * 1.01 } : p)] };

        Id(PairWindow.Build([changed], Quarter, O)).ShouldNotBe(Id(before));
    }

    [Fact]
    public void A_gap_inside_the_window_leaves_the_member_out_and_counts_it_and_too_few_sessions_is_said()
    {
        var a = Series("NSE:A", 1);
        var gap = Series("NSE:B", 2) with { Points = [.. Series("NSE:B", 2).Points.Where((_, i) => i != 150)] };

        var w = PairWindow.Build([a, gap], Quarter, O);
        var few = PairWindow.Build([Series("NSE:A", 1, count: 100)], Quarter, O);

        w.Members.Select(m => m.Symbol).ShouldBe(["NSE:A"]);
        w.Incomplete.ShouldBe(1);
        few.TooFewSessions.ShouldBeTrue();
        few.SessionsAvailable.ShouldBe(100);
    }
}
