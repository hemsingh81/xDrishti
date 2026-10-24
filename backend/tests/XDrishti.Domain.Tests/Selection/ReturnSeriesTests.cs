using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>Daily returns from closes (spec P7-09): no close after the session counts.</summary>
public sealed class ReturnSeriesTests
{
    private static readonly DateOnly D = new(2026, 10, 23);

    private static (DateOnly, double)[] Closes() => [(D.AddDays(-3), 100), (D.AddDays(-2), 110), (D.AddDays(-1), 99), (D, 99)];

    [Fact]
    public void Returns_are_close_over_the_previous_close()
    {
        var r = ReturnSeries.FromCloses(Closes(), D);

        r.Select(x => x.Date).ShouldBe([D.AddDays(-2), D.AddDays(-1), D]);
        r[0].Value.ShouldBe(0.10, 1e-12);
        r[1].Value.ShouldBe(-0.10, 1e-12);
        r[2].Value.ShouldBe(0.0, 1e-12);
    }

    [Fact]
    public void Truncation_closes_added_changed_or_removed_after_the_session_change_nothing()
    {
        var baseline = ReturnSeries.FromCloses(Closes(), D);
        var added = ReturnSeries.FromCloses([.. Closes(), (D.AddDays(1), 500), (D.AddDays(3), 1)], D);
        var changed = ReturnSeries.FromCloses([.. Closes(), (D.AddDays(1), 1)], D);
        var shuffled = ReturnSeries.FromCloses(Closes().Reverse().Concat([(D.AddDays(2), 7.0)]), D);

        added.ShouldBe(baseline);
        changed.ShouldBe(baseline);
        shuffled.ShouldBe(baseline);
    }

    [Fact]
    public void Unusable_closes_and_ambiguous_dates_are_dropped()
    {
        var r = ReturnSeries.FromCloses([(D.AddDays(-3), 100), (D.AddDays(-2), double.NaN), (D.AddDays(-2), 0), (D.AddDays(-1), 110), (D, 120), (D, 121)], D);

        r.Select(x => x.Date).ShouldBe([D.AddDays(-1)]); // -2 has no usable close; D is ambiguous
        r[0].Value.ShouldBe(0.10, 1e-12);
    }
}
