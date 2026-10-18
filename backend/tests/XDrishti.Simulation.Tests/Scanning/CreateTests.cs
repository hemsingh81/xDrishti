using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criterion 11 (the scanner's part) and 12 of P3-03a: what <c>EntryScanner.Create</c> refuses by name and what it runs, and the order guard that stays a counter of the scanner.</summary>
public sealed class CreateTests
{
    private static StrategyDefinition WithStop(StopSpec stop) => TestStrategies.OrbBuyOnly().WithBuy(b => b with { Stop = stop });

    public static TheoryData<string, StopSpec, string> Refused => new()
    {
        { "fixed_pct", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(1)), "simulation.scanner_stop_unsupported" },
        { "atr_mult", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2)), "simulation.scanner_stop_unsupported" },
        { "a buffer above 0", new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15), BufferAtr: Quantity.Of(0.5)), "simulation.scanner_stop_unsupported" },
        { "a stop timeframe other than E", new StopSpec(StopKind.SwingLow, Timeframe.H1, Lookback: Quantity.Of(3)), "simulation.scanner_stop_unsupported" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void The_stops_the_scanner_does_not_read_are_refused_by_name(string label, StopSpec stop, string code)
    {
        var side = Scan.Compile(WithStop(stop), Side.Buy);

        var created = EntryScanner.Create(side, Scan.Instrument);

        created.IsFailure.ShouldBeTrue(label);
        created.Error.Code.ShouldBe(code);
        created.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    private static StrategyDefinition Swing(Timeframe timeframe) => new(
        StrategyDefinition.CurrentSchema,
        "SWING_TEST",
        Style.Swing,
        [timeframe],
        [Side.Buy],
        [],
        new SideRules(null, TestStrategies.All("close > 0"), null, new EntrySpec(EntryKind.AtClose), new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(10)), [TestStrategies.Rr(3.0)], null),
        null,
        new TimeRules(null, null, null, 5),
        []);

    [Fact]
    public void A_daily_evaluation_is_refused_by_name()
    {
        var created = EntryScanner.Create(Scan.Compile(Swing(Timeframe.D1), Side.Buy, Timeframe.D1), Scan.Instrument);

        created.Error.Code.ShouldBe("simulation.scanner_timeframe_unsupported");
    }

    /// <summary>The guard of the scanner refuses every timeframe of a day or longer (<c>&gt;= D1</c>); the compiler already refuses a weekly or monthly evaluation before a side exists, so the guard is defence in depth and this is the proof of the first line.</summary>
    [Theory]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void A_weekly_or_monthly_evaluation_never_reaches_the_scanner_because_the_compiler_refuses_it(Timeframe timeframe)
    {
        var compiled = StrategyCompiler.Compile(Swing(timeframe), timeframe, Side.Buy);

        compiled.IsSuccess.ShouldBeFalse();
        compiled.Problems.ShouldContain(p => p.Problem.Code == "strategy.model.bad_timeframe");
    }

    [Theory]
    [InlineData(StopKind.OrOpposite)]
    [InlineData(StopKind.SwingLow)]
    [InlineData(StopKind.BarLow)]
    public void The_level_stops_of_the_buy_side_run(StopKind kind)
    {
        var stop = kind switch
        {
            StopKind.OrOpposite => new StopSpec(kind, Minutes: Quantity.Of(15)),
            StopKind.SwingLow => new StopSpec(kind, Lookback: Quantity.Of(2)),
            _ => new StopSpec(kind),
        };

        var scanner = Scan.Run(WithStop(stop), Side.Buy, Fixtures.F1().Bars());

        scanner.Candidates.Count.ShouldBe(1);
        scanner.Candidates[0].StopLevel.ShouldBe(kind switch
        {
            StopKind.OrOpposite => 98.00,
            StopKind.SwingLow => 98.00,
            _ => 99.00,
        });
    }

    [Theory]
    [InlineData(StopKind.SwingHigh)]
    [InlineData(StopKind.BarHigh)]
    public void The_level_stops_of_the_sell_side_run(StopKind kind)
    {
        var sell = OrbV1.Create().Sell! with { Stop = kind == StopKind.SwingHigh ? new StopSpec(kind, Lookback: Quantity.Of(2)) : new StopSpec(kind) };
        var definition = OrbV1.Create() with { Sell = sell };

        var scanner = Scan.Run(definition, Side.Sell, Fixtures.F12().Bars());

        scanner.Candidates.Single().StopLevel.ShouldBe(kind == StopKind.SwingHigh ? 100.00 : 99.00);
    }

    [Fact]
    public void A_null_side_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => EntryScanner.Create(null!, Scan.Instrument));
    }

    [Fact]
    public void A_repeated_minute_is_ignored_and_counted_by_the_scanner_itself()
    {
        var scanner = Scan.Scanner(Scan.Compile(OrbV1.Create(), Side.Buy));
        var bars = Fixtures.F1().Bars();

        scanner.Step(bars[0]);
        scanner.Step(bars[0]);
        scanner.Step(bars[1]);

        var c = scanner.Counters;
        (c.Bars, c.IgnoredBars).ShouldBe((2, 1));
    }
}
