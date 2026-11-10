using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>BK-06 profile-level rules: the difference to the index, the membership the hold rests on, turnover scope and correlation sampling.</summary>
public sealed class BasketProfileTests
{
    private static readonly BasketProfileOptions Options = new();

    private static DateOnly D(int i) => BasketAnalyticsTests.D(i);

    private static MemberSeries Two(string s, double a, double b) => new(s, [new(D(0), a), new(D(80), b)], [], false, false);

    private static BasketProfile Profile(MemberSeries[] members, Func<DateOnly, IReadOnlySet<string>> at, double benchFrom, double? benchTo)
    {
        List<DailyClose> bench = [new(D(0), benchFrom)];
        if (benchTo is { } to)
        {
            bench.Add(new DailyClose(D(80), to));
        }

        return BasketAnalytics.Profile(new BasketProfileInput(members, at, [D(0)], bench, new Dictionary<DateOnly, int?>(), new ProfileWindow(D(0), D(80))), Options);
    }

    private static HashSet<string> Set(params string[] symbols) => symbols.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void The_difference_to_the_index_is_the_hold_minus_the_index_return_and_unavailable_when_either_has_no_value()
    {
        MemberSeries[] abc = [Two("A", 100, 110), Two("B", 50, 45), Two("C", 200, 250)];
        var all = Set("A", "B", "C");

        var p = Profile(abc, _ => all, 1000, 1100);

        p.HoldReturnPct.Number!.Value.ShouldBe(8.3333, 1e-3);
        p.IndexReturnPct.Number!.Value.ShouldBe(10.0, 1e-9);
        p.DifferenceToIndex.Number!.Value.ShouldBe(-1.6667, 1e-3);
        Profile(abc, _ => all, 1000, null).DifferenceToIndex.State.ShouldBe(MetricState.Unavailable);
        Profile([], _ => all, 1000, 1100).DifferenceToIndex.State.ShouldBe(MetricState.Unavailable);
    }

    [Fact]
    public void The_hold_and_contribution_rest_on_the_membership_at_the_window_start_not_the_end()
    {
        MemberSeries[] members = [Two("A", 100, 110), Two("B", 50, 45), Two("C", 200, 250), Two("D", 100, 120)];

        var p = Profile(members, d => d >= D(40) ? Set("A", "B", "C", "D") : Set("A", "B", "C"), 1000, 1100);

        p.HoldReturnPct.Number!.Value.ShouldBe(8.3333, 1e-3);
        p.Contribution.Rows.Select(r => r.Symbol).ShouldNotContain("D");
    }

    [Fact]
    public void Turnover_averages_only_the_snapshots_in_the_window_and_the_one_in_force_at_its_start()
    {
        IReadOnlySet<string> At(DateOnly d) => d < D(-10) ? Set("X", "Y") : d < D(20) ? Set("A", "B", "C", "D") : Set("A", "B", "C", "E");

        var r = MemberTurnover.Compute([D(-30), D(-5), D(20)], At, new ProfileWindow(D(0), D(40)));

        r.Adds.ShouldBe(1);
        r.Removes.ShouldBe(1);
        r.Turnover.Number!.Value.ShouldBe(2.0 / 4, 1e-9);
        r.FirstDate.ShouldBe(D(-30));
    }

    [Fact]
    public void Correlation_over_a_sampled_basket_says_so_and_keeps_the_most_liquid_by_adv()
    {
        var rng = new Random(13);
        var returns = new Dictionary<string, IReadOnlyList<DailyReturn>>();
        var adv = new Dictionary<string, double>();
        for (var i = 0; i < 8; i++)
        {
            returns[$"M{i}"] = [.. Enumerable.Range(0, 60).Select(d => new DailyReturn(D(d), (rng.NextDouble() - 0.5) / 10))];
            adv[$"M{i}"] = i is 1 or 3 or 5 or 6 or 7 ? 1000 + i : 10 + i;
        }

        var options = Options with { MaxCorrelationMembers = 5, MinCorrelationMembers = 3, MinCorrelationPairs = 3 };
        var sampled = PairwiseCorrelation.Compute(returns, adv, D(59), options);
        var kept = returns.Where(kv => kv.Key is "M1" or "M3" or "M5" or "M6" or "M7").ToDictionary(kv => kv.Key, kv => kv.Value);
        var expected = PairwiseCorrelation.Compute(kept, adv, D(59), options);

        sampled.Sampled.ShouldBeTrue();
        sampled.Members.ShouldBe(5);
        sampled.AveragePairwise.Number.ShouldBe(expected.AveragePairwise.Number);
        expected.Sampled.ShouldBeFalse();
    }
}
