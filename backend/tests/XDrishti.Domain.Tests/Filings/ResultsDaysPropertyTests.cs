using XDrishti.Domain.Filings;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>
/// P15-04 criteria 6 and 10 (the guard of the look-ahead rule through the calendar), at least 250 generated cases per property, fixed seed: (a) adding, changing or removing any row with <c>intimation + lag &gt; t</c> leaves the answer at t identical; (b) control: changing a visible
/// current row does change it; (c) the order of the rows never matters.
/// </summary>
public sealed class ResultsDaysPropertyTests
{
    private const int Cases = 250;

    private static readonly DateTimeOffset Origin = At(1, 1);

    private static readonly Domain.Market.Calendar.TradingCalendar Calendar = new();

    private static readonly int[] LagMinutes = [0, 5, 5, 30, 60];

    private sealed record World(List<CalendarRow> Rows, TimeSpan Lag, DateTimeOffset T);

    private static World Generate(Random random)
    {
        var lag = TimeSpan.FromMinutes(LagMinutes[random.Next(LagMinutes.Length)]);
        var rows = new List<CalendarRow>();
        var n = 0;
        for (var p = 0; p < random.Next(1, 5); p++)
        {
            var period = new DateOnly(2025, 12, 31).AddMonths(3 * p);
            var time = Origin.AddMinutes(random.Next(0, 9000));
            CalendarRow? previous = null;
            for (var c = 0; c < random.Next(1, 4); c++)
            {
                time = time.AddMinutes(random.Next(1, 4000));
                var status = random.Next(5) == 0 ? MeetingRow.Cancelled : c == 0 ? MeetingRow.Scheduled : MeetingRow.Rescheduled;
                var row = Row(++n, new DateOnly(2026, 1, 1).AddDays(random.Next(0, 330)), time, status, period: period, supersedes: previous?.Filing.Id);
                rows.Add(row);
                previous = row;
            }
        }

        if (random.Next(3) == 0)
        {
            rows.Add(Row(++n, new DateOnly(2026, 6, 1), null));
        }

        return new World(rows, lag, Origin.AddMinutes(random.Next(0, 330 * 24 * 60)));
    }

    private static string Serialize(ResultsAnswer a) => $"{a.State}|{a.Days}|{a.MeetingDate}|{a.EventSession}|{a.PeriodEnd}|{a.Reason}";

    private static bool Visible(CalendarRow r, World w) => r.Filing.BroadcastTs is { } ts && ts + w.Lag <= w.T;

    [Fact]
    public void A_row_that_is_not_yet_known_can_be_added_changed_or_removed_without_changing_the_answer()
    {
        var random = new Random(15);
        for (var i = 0; i < Cases; i++)
        {
            var w = Generate(random);
            var baseline = Serialize(ResultsDays.Compute(w.Rows.Where(r => Visible(r, w)), w.T, w.Lag, Calendar));

            Serialize(ResultsDays.Compute(w.Rows, w.T, w.Lag, Calendar)).ShouldBe(baseline, $"case {i}: hidden rows included");
            Serialize(ResultsDays.Compute(w.Rows.Select(r => Visible(r, w) ? r : r with { MeetingDate = r.MeetingDate.AddDays(-30) }), w.T, w.Lag, Calendar)).ShouldBe(baseline, $"case {i}: hidden rows moved");
            Serialize(ResultsDays.Compute(w.Rows.Select(r => Visible(r, w) ? r : r with { Status = MeetingRow.Cancelled }), w.T, w.Lag, Calendar)).ShouldBe(baseline, $"case {i}: hidden rows cancelled");
            var extra = Row(1000 + i, Domain.Market.IndiaTime.Today(w.T), w.T - w.Lag + TimeSpan.FromTicks(10), period: new DateOnly(2030, 3, 31));
            Serialize(ResultsDays.Compute(w.Rows.Append(extra), w.T, w.Lag, Calendar)).ShouldBe(baseline, $"case {i}: a row one tick too late");
        }
    }

    [Fact]
    public void Control_a_row_that_is_known_changes_the_answer_when_it_is_the_next_meeting()
    {
        var random = new Random(16);
        var changed = 0;
        var trials = 0;
        for (var i = 0; i < Cases; i++)
        {
            var w = Generate(random);
            var today = Domain.Market.IndiaTime.Today(w.T);
            if (!Calendar.IsTradingDay(today))
            {
                continue;
            }

            trials++;
            var baseline = ResultsDays.Compute(w.Rows, w.T, w.Lag, Calendar);
            var known = Row(2000 + i, today, w.T - w.Lag, period: new DateOnly(2030, 3, 31));

            var withKnown = ResultsDays.Compute(w.Rows.Append(known), w.T, w.Lag, Calendar);

            (withKnown.State, withKnown.Days).ShouldBe(("value", 0), $"case {i}");
            changed += Serialize(withKnown) == Serialize(baseline) ? 0 : 1;
        }

        trials.ShouldBeGreaterThan(100);
        changed.ShouldBeGreaterThan(trials / 2);
    }

    [Fact]
    public void The_order_of_the_rows_never_changes_the_answer_or_the_agenda()
    {
        var random = new Random(17);
        for (var i = 0; i < Cases; i++)
        {
            var w = Generate(random);
            var shuffled = w.Rows.OrderBy(_ => random.Next()).ToList();

            Serialize(ResultsDays.Compute(shuffled, w.T, w.Lag, Calendar)).ShouldBe(Serialize(ResultsDays.Compute(w.Rows, w.T, w.Lag, Calendar)), $"case {i}");
            ResultsDays.Upcoming(shuffled, w.T, w.Lag, Calendar).ShouldBe(ResultsDays.Upcoming(w.Rows, w.T, w.Lag, Calendar), $"case {i}");
        }
    }
}
