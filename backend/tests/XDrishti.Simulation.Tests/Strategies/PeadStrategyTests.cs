using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// P15-09 criteria 5 and 6: the built-in <c>PEAD</c> lists its three parameters and refuses any other value; the buckets give the variant and the control with otherwise identical definitions; the four variants and the control have five distinct definition hashes and
/// run keys; and the strategy run end to end on a fixture takes the top quintile (decided at the Monday close, filled at the Tuesday open), leaves the middle for the control, and takes nothing when the surprise is unknown.
/// </summary>
public sealed class PeadStrategyTests
{
    private static readonly Guid InstrumentId = Scan.Instrument.Id;

    private static readonly DateOnly PreviousPeriod = new(2025, 9, 30);

    private static Dictionary<string, double> Parameters(double measure, double hold, double buckets) =>
        new() { [PeadV1.MeasureParam] = measure, [PeadV1.HoldParam] = hold, [PeadV1.BucketsParam] = buckets };

    private static PreparedStrategy Prepare(double measure, double hold, double buckets) =>
        BuiltInStrategies.Prepare("PEAD", Parameters(measure, hold, buckets), Timeframe.D1).Strategy.ShouldNotBeNull();

    [Fact]
    public void The_catalog_lists_pead_with_one_timeframe_and_the_three_parameters_and_their_closed_values()
    {
        var entry = BuiltInStrategies.Catalog.Single(c => c.Id == "PEAD");

        entry.Timeframes.ShouldBe([Timeframe.D1]);
        entry.Parameters.Select(p => (p.Name, p.Default)).ShouldBe([("measure", 1.0), ("hold", 20.0), ("buckets", 1.0)]);
        entry.Parameters.Select(p => p.Allowed).ShouldBe([[1.0, 2.0], [5.0, 20.0], [1.0, 2.0]]);
        entry.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("measure", 3)]
    [InlineData("measure", 0)]
    [InlineData("measure", 1.5)]
    [InlineData("hold", 10)]
    [InlineData("hold", 6)]
    [InlineData("hold", 21)]
    [InlineData("buckets", 0)]
    [InlineData("buckets", 3)]
    public void A_value_outside_the_closed_set_is_refused_with_the_set_in_the_message(string name, double value)
    {
        var built = BuiltInStrategies.Build("PEAD", new Dictionary<string, double> { [name] = value });

        built.Definition.ShouldBeNull();
        var problem = built.Problems.ShouldHaveSingleItem();
        problem.Problem.Code.ShouldBe(SimulationErrors.ParameterOutOfRangeCode);
        problem.Path.ShouldBe("parameters." + name);
        problem.Problem.Message.ShouldContain("x in {");
    }

    [Fact]
    public void An_unknown_parameter_is_refused_by_name_and_the_defaults_are_the_top_quintile_of_sue_held_twenty_sessions()
    {
        BuiltInStrategies.Build("PEAD", new Dictionary<string, double> { ["rr"] = 2 }).Problems.ShouldHaveSingleItem().Problem.Code.ShouldBe(SimulationErrors.UnknownParameterCode);

        var defaults = BuiltInStrategies.Build("PEAD", null);

        defaults.Definition.ShouldNotBeNull();
        defaults.Parameters.ShouldBe(new SortedDictionary<string, double> { ["buckets"] = 1, ["hold"] = 20, ["measure"] = 1 });
    }

    [Fact]
    public void The_variant_and_the_control_differ_in_the_buckets_alone()
    {
        var top = PeadV1.Create(PeadV1.MeasureSue, 20, PeadV1.BucketsTop);
        var middle = PeadV1.Create(PeadV1.MeasureSue, 20, PeadV1.BucketsMiddle);

        top.Params.Select(p => (p.Name, p.Value)).ShouldBe([("q_low", 5.0), ("q_high", 5.0), ("stop_atr", 2.0), ("rr", 10.0)]);
        middle.Params.Select(p => (p.Name, p.Value)).ShouldBe([("q_low", 2.0), ("q_high", 4.0), ("stop_atr", 2.0), ("rr", 10.0)]);
        top.Buy!.Trigger!.Items.ShouldBe(middle.Buy!.Trigger!.Items);
        top.Buy.Trigger.Items.ShouldBe(["days_since_results == 0", "results_sue_q >= q_low", "results_sue_q <= q_high"]);
        top.Buy.Stop.ShouldBe(middle.Buy.Stop);
        top.Buy.Entry.ShouldBe(middle.Buy.Entry);
        top.TimeRules.ShouldBe(middle.TimeRules);
        top.Filters.ShouldBeEmpty("no not_event_day: under the fail-closed rule it would block nearly every entry");
        top.Sides.ShouldBe([Side.Buy], "long only: a swing SELL needs futures and is a research label");
    }

    [Fact]
    public void The_four_variants_and_the_control_have_five_distinct_hashes_and_run_keys()
    {
        var prepared = new[] { Prepare(1, 5, 1), Prepare(1, 20, 1), Prepare(2, 5, 1), Prepare(2, 20, 1), Prepare(1, 20, 2) };
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(InstrumentId, "TEST")]);

        string Key(PreparedStrategy strategy) => RunAssembler.KeyText(
            new RunHeader(strategy.Record, SimulationEngine.Version, Mon, NextMon, ReplayOptions.Default, false, strategy.Fills.ToCanonicalJson(), universe, Rep.DefaultCalendar), [], []);

        prepared.Select(p => p.Record.DefinitionSha256).Distinct().Count().ShouldBe(5);
        prepared.Select(Key).Distinct().Count().ShouldBe(5);
        prepared.Select(p => p.Record.Parameters["hold"]).ShouldBe([5.0, 20.0, 5.0, 20.0, 20.0]);
    }

    [Fact]
    public void The_strategy_is_a_long_only_swing_on_1D_that_reads_the_results_days_and_the_surprise_and_holds_over_five_sessions()
    {
        var prepared = Prepare(2, 20, 1);

        prepared.HoldsOvernight.ShouldBeTrue();
        prepared.ReadsResultsDays.ShouldBeTrue();
        prepared.ReadsSurprise.ShouldBeTrue();
        prepared.ReadsEvents.ShouldBeFalse();
        prepared.ReadsContext.ShouldBeFalse();
        prepared.Record.Sides.ShouldBe([Side.Buy]);
        prepared.Record.EvaluationTimeframe.ShouldBe(Timeframe.D1);
    }

    // ---- end to end ------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static Bar[] Fixture()
    {
        var weekdays = new List<DateOnly>();
        for (var date = new DateOnly(2026, 2, 2); date <= new DateOnly(2026, 3, 13); date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                weekdays.Add(date);
            }
        }

        // flat sessions with a range of 4 around 100 (ATR(14) = 4), the same through the reaction session on Monday 2026-03-02
        return Day.Join([.. weekdays.Select(d => Day.Dense(d).Start(100.00).Set("10:00 100.00/102.00/98.00/100.00"))]);
    }

    private static ResultsDayInputs Inputs(double sue, double car3 = 0)
    {
        var broadcast = new DateTimeOffset(2026, 2, 27, 16, 12, 0, EventRows.Ist);
        var previous = Enumerable.Range(1, 200).Select(n => new SurpriseObservation(Guid.CreateVersion7(), PreviousPeriod, new DateTimeOffset(2025, 11, 3, 16, 0, 0, EventRows.Ist).AddHours(n), n, n));
        var current = new SurpriseObservation(InstrumentId, EventRows.Period, broadcast, sue, car3);
        var surprises = SurpriseInputs.Create([.. previous, current], TimeSpan.FromMinutes(5), DefaultWeek());
        return ResultsDayInputs.Create(null, [new ResultsFilingTime(InstrumentId, EventRows.Period, broadcast)], 5, surprises);
    }

    private static SimulationResult Run(double measure, double buckets, ResultsDayInputs inputs, double hold = 5)
    {
        var simulator = Simulator.Create(PeadV1.Create(measure, hold, buckets), Timeframe.D1, Cand.NoSlippage).Simulator.ShouldNotBeNull();
        var result = simulator.Run(Scan.Instrument, Fixture(), Sim.Tick, DefaultWeek(), null, null, null, inputs);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public void A_top_quintile_surprise_enters_at_the_tuesday_open_after_the_monday_close_with_a_two_atr_stop()
    {
        var result = Run(PeadV1.MeasureSue, PeadV1.BucketsTop, Inputs(sue: 170));

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.DecisionEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("15:30")));
        trade.EntryEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("09:15")));
        trade.TradeDate.ShouldBe(Tue);
        trade.StopPrice.ShouldBe(92.00, 1e-9, "100 less 2 x ATR(14) of 4");
        trade.TargetPrice!.Value.ShouldBe(100.00 + (10 * 8), 1e-9, "FIXED_RR 10 on a risk of 8");
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.SessionsHeld.ShouldBe(5);
    }

    [Fact]
    public void The_control_takes_the_middle_quintiles_and_leaves_the_top_and_the_bottom()
    {
        Run(PeadV1.MeasureSue, PeadV1.BucketsMiddle, Inputs(sue: 100)).Trades.ShouldHaveSingleItem();
        Run(PeadV1.MeasureSue, PeadV1.BucketsMiddle, Inputs(sue: 170)).Trades.ShouldBeEmpty();
        Run(PeadV1.MeasureSue, PeadV1.BucketsMiddle, Inputs(sue: 5)).Trades.ShouldBeEmpty();
        Run(PeadV1.MeasureSue, PeadV1.BucketsTop, Inputs(sue: 100)).Trades.ShouldBeEmpty();
    }

    [Fact]
    public void The_car3_measure_reads_its_own_quintile()
    {
        Run(PeadV1.MeasureCar3, PeadV1.BucketsTop, Inputs(sue: 5, car3: 170)).Trades.ShouldHaveSingleItem();
        Run(PeadV1.MeasureCar3, PeadV1.BucketsTop, Inputs(sue: 170, car3: 5)).Trades.ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_surprise_takes_no_entry_because_unknown_is_not_a_bucket()
    {
        Run(PeadV1.MeasureSue, PeadV1.BucketsTop, Inputs(sue: double.NaN)).Trades.ShouldBeEmpty();
        Run(PeadV1.MeasureSue, PeadV1.BucketsMiddle, Inputs(sue: double.NaN)).Trades.ShouldBeEmpty();
    }
}
