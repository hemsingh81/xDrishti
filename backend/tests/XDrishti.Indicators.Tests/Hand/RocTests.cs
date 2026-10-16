using XDrishti.Indicators.Tests.Support;
using static XDrishti.Indicators.Tests.Support.HandRun;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for roc: 100 · (x − x[n]) / x[n], ready at bar n + 1, not ready while the base is 0.</summary>
public sealed class RocTests
{
    [Fact]
    public void The_rate_of_change_compares_with_the_value_n_bars_ago_from_bar_n_plus_one()
    {
        var roc = new Roc(2);

        var values = Run(roc, Minutes.Closes(Day, 4, 8, 5, 6, 7));

        values.Take(2).ShouldAllBe(v => double.IsNaN(v));
        values[2].ShouldBe(25.0, 1e-12);        // 100 * (5 - 4) / 4
        values[3].ShouldBe(-25.0, 1e-12);       // 100 * (6 - 8) / 8
        values[4].ShouldBe(40.0, 1e-12);        // 100 * (7 - 5) / 5
        roc.WarmUp.ShouldBe(3);
    }

    [Fact]
    public void A_zero_base_is_not_a_number_and_not_ready_until_the_base_moves_on()
    {
        var roc = new Roc(2);

        var values = Run(roc, Minutes.Closes(Day, 4, 0, 5, 6, 7));

        values[2].ShouldBe(25.0, 1e-12);
        values[3].ShouldBeNaN();               // base is the 0 two bars ago
        roc.IsReady.ShouldBeTrue();            // the last bar has base 5
        values[4].ShouldBe(40.0, 1e-12);
        Run(new Roc(1), Minutes.Closes(Day, 0, 3))[1].ShouldBeNaN();
        new Roc(1).IsReady.ShouldBeFalse();
    }

    [Fact]
    public void The_chosen_source_is_read()
    {
        var bars = Candles((10, 20, 5, 15), (10, 30, 10, 15));

        Run(new Roc(1, PriceSource.High), bars)[1].ShouldBe(50.0, 1e-12);
        Run(new Roc(1, PriceSource.Low), bars)[1].ShouldBe(100.0, 1e-12);
    }
}
