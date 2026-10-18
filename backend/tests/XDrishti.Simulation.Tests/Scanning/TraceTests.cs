using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criterion 3 of P3-03a: the decision trace lists the items in the order of the compiled side, each with path, text, truth and the values of its features at lags 0 to the history depth minus 1.</summary>
public sealed class TraceTests
{
    [Fact]
    public void The_trace_of_the_buy_candidate_of_f1_has_the_trigger_with_its_values()
    {
        var candidate = Scan.Both(OrbV1.Create(), Fixtures.F1().Bars())[0].Candidates.Single();

        var item = candidate.Trace.Items.ShouldHaveSingleItem();
        (item.Path, item.Source, item.Truth).ShouldBe(("buy.trigger[0]", OrbV1.BuyTrigger, TraceTruth.True));
        item.Values.Select(v => v.Feature).ShouldBe(["close@15m", "or_high(15)@15m"]);
        item.Values[0].Lags.ShouldBe([100.50, 99.00]);
        item.Values[1].Lags.ShouldBe([100.00, 100.00]);
    }

    [Fact]
    public void The_trace_of_the_sell_candidate_of_f12_reads_the_range_low()
    {
        var candidate = Scan.Both(OrbV1.Create(), Fixtures.F12().Bars())[1].Candidates.Single();

        var item = candidate.Trace.Items.ShouldHaveSingleItem();
        (item.Path, item.Truth).ShouldBe(("sell.trigger[0]", TraceTruth.True));
        item.Values.Select(v => v.Feature).ShouldBe(["close@15m", "or_low(15)@15m"]);
        item.Values[0].Lags.ShouldBe([97.50, 99.00]);
        item.Values[1].Lags.ShouldBe([98.00, 98.00]);
    }

    [Fact]
    public void The_trace_lists_setup_then_trigger_then_invalidate_whatever_their_truth()
    {
        var definition = TestStrategies.OrbBuyOnly().WithBuy(b => b with { Setup = TestStrategies.All("time >= \"09:30\""), Invalidate = TestStrategies.All("low < 90") });

        var candidate = Scan.Both(definition, Fixtures.F1().Bars())[0].Candidates.Single();

        candidate.Trace.Items.Select(i => (i.Path, i.Truth)).ShouldBe([("buy.setup[0]", TraceTruth.True), ("buy.trigger[0]", TraceTruth.True), ("buy.invalidate[0]", TraceTruth.False)]);
        candidate.Trace.Items[1].Values.Count.ShouldBe(2);
        candidate.Trace.Items[0].Values.ShouldBeEmpty("time reads no feature");
        candidate.Trace.Items[2].Values.Select(v => v.Feature).ShouldBe(["low@15m"]);
    }

    [Fact]
    public void A_value_with_no_history_is_nan()
    {
        // the first decision of the day, at 09:30, with a trigger that reads one bar back, has no bar before it
        var definition = TestStrategies.OrbBuyOnly().WithBuy(b => b with { Trigger = TestStrategies.All("close > 0 or close[1] > 0") });

        var candidate = Scan.Both(definition, Fixtures.F8().Bars())[0].Candidates[0];

        candidate.Trace.Items.Single().Values.Single().Lags.ShouldBe([99.00, double.NaN]);
    }
}
