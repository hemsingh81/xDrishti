using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-04 criterion 7: the shareholding as known at an instant, with the previous quarter beside the current one.</summary>
public sealed class ShareholdingAsOfRuleTests
{
    private static readonly TimeSpan Lag = TimeSpan.FromMinutes(5);

    private static readonly DateTimeOffset Q1Time = new(2026, 7, 20, 12, 30, 0, TimeSpan.Zero);

    private static ShareholdingEnvelope Envelope(int n, DateOnly period, DateTimeOffset ts, decimal promoter, Guid? supersedes = null)
    {
        var filing = FilingSamples.Ok(n, period, ts, supersedes, FilingKind.Shareholding);
        return new ShareholdingEnvelope(filing, [new ShareholdingLine("promoter", promoter, null), new ShareholdingLine("fii", 100 - promoter, null)]);
    }

    [Fact]
    public void Nothing_visible_is_unavailable_no_entry()
    {
        var answer = ShareholdingAsOfRule.Compute([], Q1Time, Lag);

        (answer.State, answer.Reason, answer.Current).ShouldBe(("unavailable", "no_entry", null));
    }

    [Fact]
    public void The_newest_visible_period_is_current_and_the_one_before_is_previous_and_a_later_group_of_the_same_period_supersedes_from_its_own_time()
    {
        var q4 = Envelope(1, new DateOnly(2026, 3, 31), Q1Time.AddDays(-90), 50);
        var q1 = Envelope(2, new DateOnly(2026, 6, 30), Q1Time, 51);
        var q1b = Envelope(3, new DateOnly(2026, 6, 30), Q1Time.AddDays(10), 52, q1.Filing.Id);
        var all = new[] { q4, q1, q1b };

        var before = ShareholdingAsOfRule.Compute(all, Q1Time.Add(Lag).AddDays(5), Lag);
        var after = ShareholdingAsOfRule.Compute(all, Q1Time.Add(Lag).AddDays(11), Lag);
        var early = ShareholdingAsOfRule.Compute(all, Q1Time.Add(Lag).AddSeconds(-1), Lag);

        before.Current!.Lines[0].Pct.ShouldBe(51);
        before.Previous!.PeriodEnd.ShouldBe(new DateOnly(2026, 3, 31));
        after.Current!.Lines[0].Pct.ShouldBe(52);
        (early.Current!.PeriodEnd, early.Previous).ShouldBe((new DateOnly(2026, 3, 31), null));
    }

    [Fact]
    public void A_group_with_the_four_categories_off_100_is_flagged_in_the_snapshot()
    {
        var filing = FilingSamples.Ok(1, new DateOnly(2026, 6, 30), Q1Time, kind: FilingKind.Shareholding);
        var lines = ShareholdingCsv.Categories.Select(c => new ShareholdingLine(c, 10, null)).ToList();

        ShareholdingAsOfRule.Compute([new ShareholdingEnvelope(filing, lines)], Q1Time.AddDays(1), Lag).Current!.SumOff.ShouldBeTrue();
    }

    [Fact]
    public void The_previous_group_is_exactly_one_quarter_earlier_else_none()
    {
        var q3 = Envelope(1, new DateOnly(2025, 12, 31), Q1Time.AddDays(-200), 48);
        var q1 = Envelope(2, new DateOnly(2026, 6, 30), Q1Time, 51);

        var answer = ShareholdingAsOfRule.Compute([q3, q1], Q1Time.AddDays(1), Lag);

        answer.Current!.PeriodEnd.ShouldBe(new DateOnly(2026, 6, 30));
        answer.Previous.ShouldBeNull();
    }
}
