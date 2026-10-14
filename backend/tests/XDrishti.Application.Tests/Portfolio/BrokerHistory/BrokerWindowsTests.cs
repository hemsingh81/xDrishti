using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>How the dates of a run are cut into windows and how a window that Dhan refuses is halved (ADR 0025).</summary>
public sealed class BrokerWindowsTests
{
    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    [Fact]
    public void A_period_of_up_to_thirty_dates_is_one_window()
    {
        var windows = BrokerWindows.Split(D(9, 1), D(9, 30));

        windows.ShouldBe([new DateWindow(D(9, 1), D(9, 30))]);
    }

    [Fact]
    public void The_thirty_first_date_starts_a_second_window()
    {
        var windows = BrokerWindows.Split(D(9, 1), D(10, 1));

        windows.ShouldBe([new DateWindow(D(9, 1), D(9, 30)), new DateWindow(D(10, 1), D(10, 1))]);
    }

    [Fact]
    public void The_windows_follow_each_other_without_gaps_or_overlaps_and_end_on_the_last_date()
    {
        var windows = BrokerWindows.Split(D(1, 1, 2025), D(10, 10));

        windows.Count.ShouldBe(22);
        windows[0].ShouldBe(new DateWindow(D(1, 1, 2025), D(1, 30, 2025)));
        windows[^1].To.ShouldBe(D(10, 10));
        windows.Zip(windows.Skip(1)).ShouldAllBe(pair => pair.Second.From == pair.First.To.AddDays(1));
        windows.ShouldAllBe(w => w.Days <= BrokerWindows.MaxDays);
        windows.Sum(w => w.Days).ShouldBe(D(10, 10).DayNumber - D(1, 1, 2025).DayNumber + 1);
    }

    [Fact]
    public void A_single_date_is_a_window_of_one_day()
    {
        BrokerWindows.Split(D(9, 5), D(9, 5)).ShouldBe([new DateWindow(D(9, 5), D(9, 5))]);
    }

    [Fact]
    public void Nothing_is_read_when_the_start_is_after_the_end()
    {
        BrokerWindows.Split(D(9, 6), D(9, 5)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(30, 15)]
    [InlineData(29, 15)]
    [InlineData(2, 1)]
    public void A_window_is_halved_into_two_that_cover_it_exactly(int days, int firstDays)
    {
        var window = new DateWindow(D(9, 1), D(9, 1).AddDays(days - 1));

        var (first, second) = BrokerWindows.Halve(window)!.Value;

        first.Days.ShouldBe(firstDays);
        first.From.ShouldBe(window.From);
        second.From.ShouldBe(first.To.AddDays(1));
        second.To.ShouldBe(window.To);
        (first.Days + second.Days).ShouldBe(days);
    }

    [Fact]
    public void A_window_of_one_day_cannot_be_halved()
    {
        BrokerWindows.Halve(new DateWindow(D(9, 1), D(9, 1))).ShouldBeNull();
    }
}
