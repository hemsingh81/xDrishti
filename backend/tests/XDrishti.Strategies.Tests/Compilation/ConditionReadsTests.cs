using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>P3-04d: the feature reads of a condition with their lags, in the order of the text.</summary>
public sealed class ConditionReadsTests
{
    private static string[] Reads(string source, IReadOnlyDictionary<string, double>? parameters = null) =>
        [.. Compiling.Condition(source, parameters: parameters).Reads.Select(r => r.Name + "@" + r.Timeframe.ToCode() + "[" + r.Lag + "]")];

    [Fact]
    public void A_lagged_comparison_lists_each_read_in_text_order_with_its_lag()
    {
        var reads = Reads("close > or_high(or_min) and close[1] <= or_high(or_min)", Compiling.Params(("or_min", 15)));

        reads.ShouldBe(["close@15m[0]", "or_high(15)@15m[0]", "close@15m[1]", "or_high(15)@15m[0]"]);
    }

    [Fact]
    public void A_crossing_reads_both_operands_now_and_one_bar_back()
    {
        Reads("crosses_above(close, vwap)").ShouldBe(["close@15m[0]", "vwap@15m[0]", "close@15m[1]", "vwap@15m[1]"]);
    }

    [Fact]
    public void A_run_reads_the_operand_at_every_lag_up_to_its_count()
    {
        Reads("rising(close, 2)").ShouldBe(["close@15m[0]", "close@15m[1]", "close@15m[2]"]);
    }

    [Fact]
    public void A_daily_read_keeps_its_own_timeframe()
    {
        Reads("tf(\"1D\").close > tf(\"1D\").ema(20)").ShouldBe(["close@1D[0]", "ema(20)@1D[0]"]);
    }

    [Fact]
    public void A_condition_that_reads_no_feature_has_no_reads()
    {
        Compiling.Condition("time >= \"09:45\"").Reads.ShouldBeEmpty();
    }

    [Fact]
    public void The_feature_index_points_into_the_features_of_the_condition()
    {
        var condition = Compiling.Condition("close > ema(20) and close[1] <= ema(20)", Timeframe.M15);

        condition.Reads.Select(r => condition.Features[r.FeatureIndex].Name).ShouldBe(["close", "ema", "close", "ema"]);
    }

    [Fact]
    public void A_long_run_is_cut_at_the_limit_and_says_so()
    {
        var condition = Compiling.Condition("rising(close, 90)");

        condition.Reads.Count.ShouldBe(CompiledCondition.MaxReads);
        condition.ReadsTruncated.ShouldBeTrue();
    }
}
