using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criteria 5, 7 and 11 of P4-03 at the simulator: a strategy with one trail is accepted and runs end to end (the scanner records the tape of exactly the features the exit reads, the walker reads it, the trades have no legs and end for
/// the reasons a trail has); two candidates, a list of two values and the policies no walker runs yet are refused by name at the right path; two exits that read the same feature hold it once.
/// </summary>
public sealed class TrailSimulatorTests
{
    private static ExitParameter P(string name, params double[] values) => new(name, [.. values.Select(Quantity.Of)]);

    private static ExitSpec Atr(params double[] mult) => new(ExitPolicy.AtrTrail, [P("mult", mult), P("activate_at_rr", 0.5), P("atr_period", 5)]);

    private static ExitSpec Structure() => new(ExitPolicy.StructureTrail, [P("lookback_bars", 3)]);

    private static ExitSpec Ema() => new(ExitPolicy.EmaTrail, [P("length", 5)]);

    private static StrategyDefinition With(params ExitSpec[] exits) => OrbV1.Create().WithBuy(b => b with { Exits = exits }) with { Sell = OrbV1.Create().Sell! with { Exits = exits } };

    public static TheoryData<string, ExitPolicy> Policies { get; } = new()
    {
        { "ATR_TRAIL", ExitPolicy.AtrTrail },
        { "STRUCTURE_TRAIL", ExitPolicy.StructureTrail },
        { "EMA_TRAIL", ExitPolicy.EmaTrail },
    };

    private static ExitSpec SpecOf(ExitPolicy policy) => policy switch
    {
        ExitPolicy.AtrTrail => Atr(2),
        ExitPolicy.StructureTrail => Structure(),
        _ => Ema(),
    };

    [Theory]
    [MemberData(nameof(Policies))]
    public void A_strategy_with_one_trail_is_accepted_and_the_tape_holds_exactly_the_feature_it_reads_and_the_close(string name, ExitPolicy policy)
    {
        var definition = With(SpecOf(policy));
        var side = Scan.Compile(definition, Side.Buy);
        var scanner = Scan.Run(side, Fixtures.OrbDays.Single(f => f.Name == "F1").Bars);

        Simulator.Create(definition, Timeframe.M15).Simulator.ShouldNotBeNull(name);
        var reads = side.Exits.SelectMany(e => e.Reads).Distinct().Select(r => FeatureNames.Of(r)).ToList();
        reads.Count.ShouldBe(1);
        scanner.Tape.Features.ShouldBe(reads, name);
        scanner.Tape.Count.ShouldBeGreaterThan(10);
        Enumerable.Range(0, scanner.Tape.Count).ShouldAllBe(k => !double.IsNaN(scanner.Tape.Close(k)), "the close is recorded on every row");
    }

    [Fact]
    public void The_features_of_the_three_trails_are_the_ones_the_spec_names_atr_lowest_of_the_low_for_a_buy_highest_of_the_high_for_a_sell_and_ema()
    {
        var atr = Scan.Compile(With(Atr(2)), Side.Buy).Exits[0].Reads[0];
        var lowest = Scan.Compile(With(Structure()), Side.Buy).Exits[0].Reads[0];
        var highest = Scan.Compile(With(Structure()), Side.Sell).Exits[0].Reads[0];
        var ema = Scan.Compile(With(Ema()), Side.Buy).Exits[0].Reads[0];

        (atr.Name, FeatureNames.Of(atr)).ShouldBe(("atr", "atr(5)@15m"));
        (lowest.Name, lowest.Source, FeatureNames.Of(lowest)).ShouldBe(("lowest", PriceSource.Low, "lowest(3)@15m"));
        (highest.Name, highest.Source, FeatureNames.Of(highest)).ShouldBe(("highest", PriceSource.High, "highest(3)@15m"));
        (ema.Name, ema.Source, FeatureNames.Of(ema)).ShouldBe(("ema", PriceSource.Close, "ema(5)@15m"));
    }

    [Fact]
    public void Two_exits_that_read_the_same_feature_hold_it_once_in_the_tape()
    {
        var shared = With(Atr(2), new ExitSpec(ExitPolicy.PartialTrail, [P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2), P("atr_period", 5)]));
        var other = With(Atr(2), new ExitSpec(ExitPolicy.PartialTrail, [P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2), P("atr_period", 7)]));
        var bars = Fixtures.OrbDays.Single(f => f.Name == "F1").Bars;

        Scan.Run(Scan.Compile(shared, Side.Buy), bars).Tape.Features.Count.ShouldBe(1);
        Scan.Run(Scan.Compile(other, Side.Buy), bars).Tape.Features.Count.ShouldBe(2);
    }

    [Fact]
    public void A_strategy_with_FIXED_RR_alone_builds_no_tape()
    {
        Scan.Run(OrbV1.Create(), Side.Buy, Fixtures.OrbDays.Single(f => f.Name == "F1").Bars).Tape.ShouldBeSameAs(ExitTape.Empty);
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void A_run_of_each_trail_over_the_orb_days_gives_trades_without_legs_that_end_for_the_reasons_a_trail_has(string name, ExitPolicy policy)
    {
        var simulator = Sim.Create(With(SpecOf(policy)));
        var trades = new List<SimTrade>();
        var ignored = 0L;
        foreach (var fixture in Fixtures.OrbDays)
        {
            var result = Sim.Run(simulator, fixture.Bars);
            trades.AddRange(result.Trades);
            ignored += result.Walk.StopLoosenIgnored;
            result.Walk.LegsTotal.ShouldBe(0, name + " " + fixture.Name);
            (result.Walk.ExitsStop + result.Walk.ExitsSquareOff + result.Walk.ExitsEndOfData + result.Walk.ExitsEmaExit + result.Walk.ExitsTarget + result.Walk.ExitsMaxHold).ShouldBe(result.Walk.Trades, name + " " + fixture.Name);
        }

        trades.Count.ShouldBeGreaterThan(5, name);
        trades.ShouldAllBe(t => t.Legs == null && t.TargetPrice == null && t.ExitReason != ExitReason.Target, name);
        trades.ShouldAllBe(t => policy == ExitPolicy.EmaTrail || t.ExitReason != ExitReason.EmaExit, name);
        trades.ShouldAllBe(t => t.RExact >= -t.MaeR - 1e-9 && t.RExact <= t.MfeR + 1e-9, name + ": -MAE <= R <= MFE");
        trades.Select(t => t.ExitReason).Distinct().Count().ShouldBeGreaterThanOrEqualTo(1);
    }

    private static string[] Errors(SimulatorBuild build) => [.. build.Problems.Where(p => p.Problem.Severity == ProblemSeverity.Error).Select(p => p.Problem.Code + "@" + p.Path + ": " + p.Problem.Message)];

    [Fact]
    public void A_list_of_two_mult_values_is_refused_naming_the_policy_and_the_parameter_at_the_exits()
    {
        var build = Simulator.Create(With(Atr(2, 3)), Timeframe.M15);

        build.Simulator.ShouldBeNull();
        var errors = Errors(build);
        errors.ShouldContain(e => e.StartsWith("simulation.unsupported_exit@buy.exits", StringComparison.Ordinal) && e.Contains("ATR_TRAIL", StringComparison.Ordinal) && e.Contains("`mult`", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_candidates_are_refused_at_the_exits_even_when_both_are_runnable()
    {
        var build = Simulator.Create(With(Atr(2), Ema()), Timeframe.M15);

        build.Simulator.ShouldBeNull();
        Errors(build).ShouldContain(e => e.StartsWith("simulation.unsupported_exit@buy.exits", StringComparison.Ordinal));
        Errors(build).ShouldContain(e => e.StartsWith("simulation.unsupported_exit@sell.exits", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ExitPolicy.PartialBe)]
    [InlineData(ExitPolicy.PartialTrail)]
    [InlineData(ExitPolicy.Pyramid)]
    public void The_policies_no_walker_runs_yet_are_still_refused_by_name(ExitPolicy policy)
    {
        ExitParameter[] parameters = policy switch
        {
            ExitPolicy.PartialBe => [P("book_pct", 50), P("t1_rr", 1), P("t2_rr", 2)],
            ExitPolicy.PartialTrail => [P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2)],
            _ => [P("initial_pct", 50), P("add_at_rr", 1, 2), P("add_pct", 30, 20), P("max_adds", 2), P("trail_atr_mult", 2)],
        };

        var build = Simulator.Create(With(new ExitSpec(policy, parameters)), Timeframe.M15);

        build.Simulator.ShouldBeNull();
        Errors(build).ShouldContain(e => e.StartsWith("simulation.unsupported_exit@buy.exits", StringComparison.Ordinal) && e.Contains(policy.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void FIXED_RR_with_two_rr_values_is_still_refused_naming_rr()
    {
        var build = Simulator.Create(With(new ExitSpec(ExitPolicy.FixedRr, [P("rr", 1.5, 2)])), Timeframe.M15);

        build.Simulator.ShouldBeNull();
        Errors(build).ShouldContain(e => e.StartsWith("simulation.unsupported_exit@buy.exits", StringComparison.Ordinal) && e.Contains("`rr`", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixed_rr_runs_are_unchanged_by_the_existence_of_the_trails()
    {
        var bars = Fixtures.OrbDays.Single(f => f.Name == "F1").Bars;

        var result = Sim.Run(OrbV1.Create(), bars);

        result.Trades.Single().ExitReason.ShouldBe(ExitReason.Target);
        result.Walk.ExitsEmaExit.ShouldBe(0);
        result.Walk.StopLoosenIgnored.ShouldBe(0);
    }

    [Fact]
    public void A_swing_strategy_with_a_trail_is_accepted()
    {
        var swing = BaseBreakoutV1.Create();
        var withTrail = swing.WithBuy(b => b with { Exits = [Structure()] });

        Simulator.Create(withTrail, Timeframe.D1).Simulator.ShouldNotBeNull();
    }
}
