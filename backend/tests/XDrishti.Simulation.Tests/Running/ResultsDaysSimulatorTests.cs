using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// P15-05 criteria 6 and 7 through the real <see cref="Simulator"/>: a strategy that names <c>days_to_results</c> runs on the per-date series built from the run's inputs, is refused <c>simulation.filter_not_available</c> before any bar is read when it has none, and
/// takes <b>no</b> entry on a date where the number is unknown (<c>not (days_to_results &lt;= 2)</c> on NaN is unknown, not true; the compiled kernel is three-valued); a strategy that names neither feature gives exactly the results it gave before, whatever inputs it is handed.
/// </summary>
public sealed class ResultsDaysSimulatorTests
{
    private static readonly Guid Instrument = Scan.Instrument.Id;

    private static StrategyDefinition WithSetup(string rule) =>
        TestStrategies.OrbBuyOnly().WithBuy(b => b with { Setup = TestStrategies.All(rule) });

    private static readonly StrategyDefinition NotNearResults = WithSetup("not (days_to_results <= 2)");

    private static SimulationResult Run(StrategyDefinition definition, ResultsDayInputs? inputs)
    {
        var simulator = Sim.Create(definition, Timeframe.M15, Cand.NoSlippage);
        var result = simulator.Run(Scan.Instrument, Cases.F("F1").Bars, Sim.Tick, DefaultWeek(), null, null, null, inputs);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    private static ResultsDayInputs Meeting(Guid instrument, DateOnly meeting) =>
        ResultsDayInputs.Create(EventRows.Snapshot(instrument, meeting), [], 5);

    [Fact]
    public void A_strategy_that_names_the_feature_is_accepted_and_says_it_reads_the_results_days()
    {
        var built = Simulator.Create(NotNearResults, Timeframe.M15);

        built.Simulator.ShouldNotBeNull(string.Join("; ", built.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        built.Simulator.ReadsResultsDays.ShouldBeTrue();
        built.Simulator.ReadsEvents.ShouldBeFalse();
        Simulator.Create(TestStrategies.OrbBuyOnly(), Timeframe.M15).Simulator!.ReadsResultsDays.ShouldBeFalse();
        Simulator.Create(WithSetup("days_since_results >= 1"), Timeframe.M15).Simulator!.ReadsResultsDays.ShouldBeTrue();
    }

    [Fact]
    public void Without_inputs_the_run_is_refused_by_name_before_any_bar_is_read()
    {
        var simulator = Sim.Create(NotNearResults, Timeframe.M15, Cand.NoSlippage);
        var unordered = new[] { new Bar(120, 1, 1, 1, 1, 1), new Bar(60, 1, 1, 1, 1, 1) };

        var noInputs = simulator.Run(Scan.Instrument, unordered, Sim.Tick, DefaultWeek());
        var noCalendar = simulator.Run(Scan.Instrument, unordered, Sim.Tick, null, null, null, null, Meeting(Instrument, Mon));

        foreach (var refused in new[] { noInputs, noCalendar })
        {
            refused.IsFailure.ShouldBeTrue();
            (refused.Error.Code, refused.Error.Message).ShouldBe(("simulation.filter_not_available", "no results calendar or filings for the window (P15)"));
        }
    }

    [Fact]
    public void An_entry_far_from_the_event_is_taken_and_equals_the_trade_of_the_same_strategy_without_the_rule()
    {
        // F1 is decided on Monday 2026-03-02 and the meeting is Friday 2026-03-20: 14 sessions away at the start of the date
        var bars = Cases.F("F1").Bars;

        var ruled = Run(NotNearResults, Meeting(Instrument, new DateOnly(2026, 3, 20)));
        var plain = Sim.Run(Sim.Create(TestStrategies.OrbBuyOnly(), fills: Cand.NoSlippage), bars);

        ruled.Trades.ShouldHaveSingleItem();
        OutcomeFormatter.Render(Result.Success(ruled.Outcomes.Single().Walk)).ShouldBe(OutcomeFormatter.Render(Result.Success(plain.Outcomes.Single().Walk)));
    }

    [Fact]
    public void An_entry_on_the_event_session_is_not_taken_because_the_rule_is_false()
    {
        var result = Run(NotNearResults, Meeting(Instrument, Mon));

        result.Trades.ShouldBeEmpty();
        result.Outcomes.ShouldBeEmpty();
    }

    [Fact]
    public void A_date_with_no_known_number_takes_no_entry_because_unknown_is_not_true()
    {
        // another instrument has the only row: this instrument's series is NaN on every date. `not (NaN <= 2)` is unknown in the three-valued kernel, so no setup passes.
        var other = Meeting(Guid.NewGuid(), Mon);
        var nothingFor = ResultsDayInputs.Create(EventCalendarSnapshot.Create([], 5), [new ResultsFilingTime(Guid.NewGuid(), EventRows.Period, new DateTimeOffset(2026, 2, 1, 12, 0, 0, EventRows.Ist))], 5);

        foreach (var inputs in new[] { other, nothingFor })
        {
            var result = Run(NotNearResults, inputs);

            result.Trades.ShouldBeEmpty();
            result.Outcomes.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Days_since_results_is_read_from_the_filings_of_the_instrument()
    {
        // a filing broadcast on Friday 2026-02-27 at 16:12 reacts on Monday 2026-03-02 (the decision date): 0 sessions since; the rule `days_since_results >= 1` is false then, `<= 0` is true
        var filed = ResultsDayInputs.Create(null, [new ResultsFilingTime(Instrument, EventRows.Period, new DateTimeOffset(2026, 2, 27, 16, 12, 0, EventRows.Ist))], 5);

        Run(WithSetup("days_since_results >= 1"), filed).Trades.ShouldBeEmpty();
        Run(WithSetup("days_since_results <= 0"), filed).Trades.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_strategy_that_names_neither_feature_gives_the_same_results_with_or_without_inputs_and_never_reads_them()
    {
        foreach (var name in new[] { "F1", "F2", "F5", "F13" })
        {
            var bars = Cases.F(name).Bars;
            var simulator = Sim.Create(OrbV1.Create(), fills: Cand.NoSlippage);

            var without = simulator.Run(Scan.Instrument, bars, Sim.Tick, DefaultWeek());
            var given = simulator.Run(Scan.Instrument, bars, Sim.Tick, DefaultWeek(), null, null, null, Meeting(Instrument, Mon));

            Render.Result(given.Value).ShouldBe(Render.Result(without.Value), name);
        }
    }
}
