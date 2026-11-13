using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>AM-01b (GAP-09): a row outside the epoch's frozen evidence set is written and graded but never counts for the edge series (which the sequential test, the decay report and the readiness forward rows share).</summary>
public sealed class EdgeSeriesEvidenceSetTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);

    private static ShadowResultRow Row(string key, bool inSet) => new(Day, key, "primary", true, "graded", 1.0, "gross", null, 1.0, 3, inSet);

    [Fact]
    public void Only_rows_inside_the_set_make_the_series()
    {
        var rows = new[] { Row("A|x|1", true), Row("B|x|2", false), Row("C|x|3", true) };

        var selection = EdgeSeries.Select(rows, 3, null);

        selection.ScopedCount.ShouldBe(2);
        selection.Graded.Select(r => r.TradeKey).ShouldBe(["A|x|1", "C|x|3"]);
    }

    [Fact]
    public void An_epoch_without_a_set_counts_nothing()
    {
        var selection = EdgeSeries.Select([Row("A|x|1", false)], 3, null);

        selection.ScopedCount.ShouldBe(0);
        selection.Graded.ShouldBeEmpty();
    }
}
