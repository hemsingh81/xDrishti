using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-01 criterion 10: no successful poll for more than 30 minutes, or a failed settle, is a gap; no run at all is not_run.</summary>
public sealed class CaptureHealthTests
{
    private static readonly DateTimeOffset After = LiveFixtureDay.At("18:00");

    private static CaptureRun Poll(string time, string outcome = OutcomeCodes.RunOk) => new(OutcomeCodes.KindPoll, LiveFixtureDay.At(time), LiveFixtureDay.At(time), outcome);

    private static List<CaptureRun> Every(int minutes, string from = "09:10", string to = "15:40")
    {
        var list = new List<CaptureRun>();
        for (var t = LiveFixtureDay.At(from); t <= LiveFixtureDay.At(to); t = t.AddMinutes(minutes))
        {
            list.Add(new CaptureRun(OutcomeCodes.KindPoll, t, t, OutcomeCodes.RunOk));
        }

        return list;
    }

    [Fact]
    public void A_day_with_no_run_is_not_run() => CaptureHealth.Evaluate([], LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.NotRun);

    [Fact]
    public void Polls_every_two_minutes_are_healthy() => CaptureHealth.Evaluate(Every(2), LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.Healthy);

    [Fact]
    public void Thirty_minutes_of_silence_is_still_healthy_and_thirty_one_is_a_gap()
    {
        CaptureHealth.Evaluate(Every(30), LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.Healthy);
        CaptureHealth.Evaluate(Every(31), LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.Gap);
    }

    [Fact]
    public void Failed_polls_do_not_count_but_a_partial_one_does()
    {
        var runs = Every(2).Select(r => r.StartedAt.Hour is >= 11 and < 12 ? r with { Outcome = OutcomeCodes.RunTokenExpired } : r).ToList();
        CaptureHealth.Evaluate(runs, LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.Gap);

        var partial = runs.Select(r => r.Outcome == OutcomeCodes.RunTokenExpired ? r with { Outcome = OutcomeCodes.RunPartial } : r).ToList();
        CaptureHealth.Evaluate(partial, LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.Healthy);
    }

    [Fact]
    public void A_failed_settle_is_a_gap_and_a_later_good_one_clears_it()
    {
        var runs = Every(2);
        runs.Add(new CaptureRun(OutcomeCodes.KindSettle, LiveFixtureDay.At("17:00"), LiveFixtureDay.At("17:01"), OutcomeCodes.RunUnavailable));
        CaptureHealth.Evaluate(runs, LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.Gap);

        runs.Add(new CaptureRun(OutcomeCodes.KindSettle, LiveFixtureDay.At("17:30"), LiveFixtureDay.At("17:31"), OutcomeCodes.RunOk));
        CaptureHealth.Evaluate(runs, LiveFixtureDay.Day, After).ShouldBe(CaptureHealth.Healthy);
    }

    [Fact]
    public void Today_is_judged_up_to_now_only()
    {
        CaptureHealth.Evaluate(Every(2, to: "11:00"), LiveFixtureDay.Day, LiveFixtureDay.At("11:10")).ShouldBe(CaptureHealth.Healthy);
        CaptureHealth.Evaluate(Every(2, to: "11:00"), LiveFixtureDay.Day, LiveFixtureDay.At("11:40")).ShouldBe(CaptureHealth.Gap);
    }

    [Fact]
    public void A_late_start_is_a_gap_from_the_window_start() =>
        CaptureHealth.Evaluate([Poll("10:00")], LiveFixtureDay.Day, LiveFixtureDay.At("10:05")).ShouldBe(CaptureHealth.Gap);
}
