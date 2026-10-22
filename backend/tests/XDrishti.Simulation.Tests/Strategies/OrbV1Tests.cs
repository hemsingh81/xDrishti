using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>Criterion 1 of P3-03a: ORB v1 compiles on 15m and 5m, reads the features the compiler pins, varies by parameter, and is exactly the definition the spec writes (it is the interim version of the built-in ORB).</summary>
public sealed class OrbV1Tests
{
    private static FeatureSpec Spec(string name, Timeframe timeframe, double p1 = 0) => new(name, timeframe, PriceSource.Close, p1);

    [Fact]
    public void The_definition_is_exactly_the_one_of_the_spec()
    {
        var literal = new StrategyDefinition(
            StrategyDefinition.CurrentSchema,
            "ORB",
            Style.Intraday,
            [Timeframe.M5, Timeframe.M15],
            [Side.Buy, Side.Sell],
            [new StrategyParam("or_min", 15), new StrategyParam("rr", 2)],
            new SideRules(
                null,
                new ConditionList(ListMode.All, ["close > or_high(or_min) and close[1] <= or_high(or_min)"]),
                null,
                new EntrySpec(EntryKind.AtClose),
                new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Named("or_min")),
                [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Named("rr")])])],
                null),
            new SideRules(
                null,
                new ConditionList(ListMode.All, ["close < or_low(or_min) and close[1] >= or_low(or_min)"]),
                null,
                new EntrySpec(EntryKind.AtClose),
                new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Named("or_min")),
                [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Named("rr")])])],
                null),
            new TimeRules(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null),
            []);

        OrbV1.Create().ShouldBe(literal);
    }

    [Fact]
    public void Each_call_builds_an_equal_but_separate_definition_and_the_reward_risk_reaches_the_exit()
    {
        OrbV1.Create().ShouldBe(OrbV1.Create());
        OrbV1.Create(rewardRisk: 1.5).Params.ShouldBe([new StrategyParam("or_min", 15), new StrategyParam("rr", 1.5)]);
        OrbV1.Create(rewardRisk: 3.0).Sell!.Exits!.Single().Parameters.Single().Values.ShouldBe([Quantity.Named("rr")]);
        OrbV1.Create(orMinutes: 30).Params.ShouldBe([new StrategyParam("or_min", 30), new StrategyParam("rr", 2)]);
    }

    [Theory]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.M5)]
    public void Both_sides_compile_with_no_error_and_no_warning_on_15m_and_5m(Timeframe timeframe)
    {
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var result = StrategyCompiler.Compile(OrbV1.Create(), timeframe, side);

            result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Problems.Select(p => p.Path + " " + p.Problem.Code)));
            result.Problems.ShouldBeEmpty();
            result.Compiled!.HistoryDepth.ShouldBe(2);
            result.Compiled.NeedsContext.ShouldBeFalse();
            result.Compiled.NeedsExpiryDays.ShouldBeFalse();
        }
    }

    [Fact]
    public void The_features_of_the_buy_side_are_close_then_the_range_high_and_low_and_the_sell_side_mirrors_them()
    {
        var buy = Scan.Compile(OrbV1.Create(), Side.Buy);
        var sell = Scan.Compile(OrbV1.Create(), Side.Sell);

        buy.Features.ShouldBe([Spec("close", Timeframe.M15), Spec("or_high", Timeframe.M15, 15), Spec("or_low", Timeframe.M15, 15)]);
        sell.Features.ShouldBe([Spec("close", Timeframe.M15), Spec("or_low", Timeframe.M15, 15), Spec("or_high", Timeframe.M15, 15)]);
    }

    [Fact]
    public void Thirty_minutes_read_the_longer_range()
    {
        var side = Scan.Compile(OrbV1.Create(orMinutes: 30), Side.Buy);

        side.Features.ShouldContain(Spec("or_high", Timeframe.M15, 30));
        side.Features.ShouldContain(Spec("or_low", Timeframe.M15, 30));
    }

    [Fact]
    public void Twenty_minutes_is_refused_on_15m_with_bad_arguments_in_the_trigger_and_feature_invalid_in_the_stop_on_each_side()
    {
        foreach (var (side, prefix) in new[] { (Side.Buy, "buy"), (Side.Sell, "sell") })
        {
            var result = StrategyCompiler.Compile(OrbV1.Create(orMinutes: 20), Timeframe.M15, side);

            result.IsSuccess.ShouldBeFalse();

            // the trigger reads the range twice, so the language reports the bad argument once per call: two problems of one kind at one path (the spec counted them as one)
            result.Errors.Select(p => (p.Path, p.Problem.Code)).Distinct().ShouldBe([(prefix + ".trigger[0]", "strategy.lint.bad_arguments"), (prefix + ".stop.minutes", "strategy.model.feature_invalid")]);
        }
    }

    [Fact]
    public void Twenty_minutes_is_four_whole_bars_on_5m()
    {
        StrategyCompiler.Compile(OrbV1.Create(orMinutes: 20), Timeframe.M5, Side.Buy).IsSuccess.ShouldBeTrue();
    }
}
