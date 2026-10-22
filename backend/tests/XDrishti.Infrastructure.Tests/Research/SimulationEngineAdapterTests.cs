using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Research;
using XDrishti.Simulation;
using XDrishti.Simulation.Replay;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Tests.Shared;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>Criterion 11 of P3-04a: the adapter turns a built-in strategy id and parameters into a prepared strategy, reports every refusal as a field error with its path and code, and replays exactly as the library does.</summary>
public sealed class SimulationEngineAdapterTests
{
    private static readonly SimulationEngineAdapter Engine = new();

    private static readonly FillRules NoSlippage = FillRules.Create(0.1, SlippageConfig.None).Value;

    private static readonly Guid Id = new("00000000-0000-0000-0000-0000000000a1");

    private static Dictionary<string, double> Params(params (string Name, double Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    private static IPreparedStrategy Prepared(params (string Name, double Value)[] values) =>
        Engine.Prepare("ORB", Params(values), Timeframe.M15, NoSlippage) is { IsSuccess: true } ok ? ok.Value : throw new InvalidOperationException("The strategy was not prepared.");

    [Fact]
    public void The_engine_version_is_that_of_the_simulation_rules()
    {
        Engine.EngineVersion.ShouldBe(SimulationEngine.Version);
    }

    [Fact]
    public void The_built_ins_are_the_catalog_of_the_library()
    {
        Engine.BuiltIns.ShouldBeSameAs(BuiltInStrategies.Catalog);
        Engine.BuiltIns.Select(b => b.Id).ShouldBe(BuiltInStrategies.Ids);
    }

    [Fact]
    public void The_base_breakout_id_on_1D_records_its_four_effective_parameters()
    {
        var result = Engine.Prepare("BASE_BREAKOUT", Params(), Timeframe.D1, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Record.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ShouldBe([("base_len", 20.0), ("base_pct", 8.0), ("rr", 2.0), ("vol_x", 1.5)]);
    }

    [Fact]
    public void A_built_in_id_and_its_parameters_give_the_record_and_the_canonical_json_of_the_replay()
    {
        var result = Engine.Prepare("ORB", Params(("or_min", 30), ("rr", 3.0)), Timeframe.M15, null);

        result.IsSuccess.ShouldBeTrue();
        var record = result.Value.Record;
        record.Id.ShouldBe("ORB");
        record.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 30, ["rr"] = 3.0 });
        record.EvaluationTimeframe.ShouldBe(Timeframe.M15);
        record.Sides.ShouldBe([Side.Buy, Side.Sell]);
        var direct = StrategyPreparation.Prepare(OrbV1.Create(30, 3.0), record.Parameters, Timeframe.M15).Strategy.ShouldNotBeNull().Record;
        record.DefinitionJson.ShouldBe(direct.DefinitionJson);
        record.DefinitionSha256.ShouldBe(direct.DefinitionSha256);
        result.Value.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void No_parameters_give_the_defaults_of_the_strategy()
    {
        var record = Prepared().Record;

        record.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 15, ["rr"] = 2.0 });
        record.DefinitionJson.ShouldBe(StrategyPreparation.Prepare(OrbV1.Create(), record.Parameters, Timeframe.M15).Strategy.ShouldNotBeNull().Record.DefinitionJson);
    }

    [Fact]
    public void An_unknown_strategy_id_is_a_field_error_with_the_path_and_the_code_of_the_factory()
    {
        var result = Engine.Prepare("MACD", Params(), Timeframe.M15, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SimulationErrors.StrategyInvalidCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields.ShouldNotBeNull().Select(f => (f.Field, f.Code)).ShouldBe([("strategy_id", SimulationErrors.UnknownStrategyCode)]);
    }

    [Fact]
    public void An_unknown_parameter_is_a_field_error_for_each_name_in_name_order()
    {
        var result = Engine.Prepare("ORB", Params(("zz", 1), ("or_min", 15), ("aa", 2)), Timeframe.M15, null);

        result.Error.Fields.ShouldNotBeNull().Select(f => (f.Field, f.Code)).ShouldBe([("parameters.aa", SimulationErrors.UnknownParameterCode), ("parameters.zz", SimulationErrors.UnknownParameterCode)]);
    }

    [Fact]
    public void An_opening_range_that_is_not_a_multiple_of_the_bar_is_refused_by_the_compile_with_its_path_and_code()
    {
        var result = Engine.Prepare("ORB", Params(("or_min", 20)), Timeframe.M15, null);

        result.IsFailure.ShouldBeTrue();
        var fields = result.Error.Fields.ShouldNotBeNull();
        fields.ShouldNotBeEmpty();
        fields.ShouldAllBe(f => f.Field.Length > 0 && f.Code != null && f.Code.StartsWith("strategy.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("or_min", 0)]
    [InlineData("or_min", -15)]
    [InlineData("rr", 0)]
    [InlineData("rr", -2)]
    public void A_value_out_of_the_range_of_the_catalog_is_refused_by_the_compile_not_by_the_factory(string name, double value)
    {
        var result = Engine.Prepare("ORB", Params((name, value)), Timeframe.M15, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields.ShouldNotBeNull().ShouldAllBe(f => f.Code != null && f.Code.StartsWith("strategy.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_non_finite_parameter_is_the_callers_error_and_throws_because_the_use_case_validates_it_first()
    {
        Should.Throw<ArgumentException>(() => Engine.Prepare("ORB", Params(("or_min", double.NaN)), Timeframe.M15, null));
    }

    [Fact]
    public void More_than_twenty_problems_are_listed_twenty_and_the_message_counts_them()
    {
        var parameters = Enumerable.Range(0, 25).ToDictionary(i => "p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), i => (double)i);

        var result = Engine.Prepare("ORB", parameters, Timeframe.M15, null);

        result.Error.Fields.ShouldNotBeNull().Count.ShouldBe(20);
        result.Error.Message.ShouldContain("25");
    }

    [Fact]
    public void The_warnings_of_the_compile_become_field_errors_with_the_path_and_the_code_of_the_problem()
    {
        var problem = new LocatedProblem("params[1]", new Problem("strategy.model.param_shadows_name", "A parameter hides a name.", new SourceSpan(0, 0, 1, 1), Severity: ProblemSeverity.Warning));

        var fields = SimulationEngineAdapter.Fields([problem]);

        fields.ShouldBe([new FieldError("params[1]", "A parameter hides a name.", Code: "strategy.model.param_shadows_name")]);
    }

    [Fact]
    public void The_fill_rules_given_to_prepare_are_the_ones_the_walks_use()
    {
        var exact = Engine.Prepare("ORB", Params(), Timeframe.M15, NoSlippage).Value;
        var modelled = Engine.Prepare("ORB", Params(), Timeframe.M15, null).Value;
        var bars = ReplayDays.F1(ReplayDays.D2);

        var withNone = Engine.Replay(exact, Input(bars));
        var withDefault = Engine.Replay(modelled, Input(bars));

        withNone.Trades.Single().Fills.ShouldBe(FillBasis.Exact);
        withDefault.Trades.Single().Fills.ShouldBe(FillBasis.Modelled);
    }

    private static InstrumentReplayInput Input(Bar[] bars, SkipReason? preset = null) =>
        new(Id, "NSE:ABB", bars, AdjustmentFactors.None, null, new TradingCalendar(), ReplayOptions.Default, ReplayDays.D2, ReplayDays.D2, TickSpec.FromMasterPaise(5m).Value, preset, null);

    [Fact]
    public void The_replay_of_the_f1_day_equals_a_direct_replay_of_the_library()
    {
        var bars = ReplayDays.F1(ReplayDays.D2);
        var engine = Prepared();
        var direct = StrategyPreparation.Prepare(OrbV1.Create(), engine.Record.Parameters, Timeframe.M15, NoSlippage).Strategy.ShouldNotBeNull();

        var viaAdapter = Engine.Replay(engine, Input(bars));
        var viaLibrary = InstrumentReplayer.Replay(direct, Input(bars));

        viaAdapter.Trades.ShouldBe(viaLibrary.Trades);
        viaAdapter.OpenAtEnd.ShouldBe(viaLibrary.OpenAtEnd);
        viaAdapter.Record.BarsSha256.ShouldBe(viaLibrary.Record.BarsSha256);
        viaAdapter.Record.Fates.ShouldBe(viaLibrary.Record.Fates);
        viaAdapter.Record.Sessions.ShouldBe(viaLibrary.Record.Sessions);
        viaAdapter.Record.Scans.Select(x => x.Candidates).ShouldBe(viaLibrary.Record.Scans.Select(x => x.Candidates));
        var trade = viaAdapter.Trades.Single();
        (trade.EntryPrice, trade.StopPrice, trade.TargetPrice, trade.ExitPrice, trade.ExitReason, trade.RExact).ShouldBe((100.50, 98.00, 105.50, 105.50, ExitReason.Target, 2.0));
    }

    [Fact]
    public void A_preset_skip_is_recorded_without_a_scan()
    {
        var replay = Engine.Replay(Prepared(), Input(ReplayDays.F1(ReplayDays.D2), SkipReason.CsvAdjustedTwice));

        replay.Record.Skipped.ShouldBe(SkipReason.CsvAdjustedTwice);
        replay.Trades.ShouldBeEmpty();
        replay.Record.BarsSha256.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void A_handle_that_the_adapter_did_not_make_is_refused()
    {
        var foreign = new ForeignHandle();

        Should.Throw<ArgumentException>(() => Engine.Replay(foreign, Input(ReplayDays.F1(ReplayDays.D2))));
    }

    [Fact]
    public void A_caller_error_of_the_replay_passes_through_as_the_library_throws_it()
    {
        var bars = ReplayDays.F1(ReplayDays.D2);
        var wrongOrder = new[] { bars[1], bars[0] };

        Should.Throw<InvalidOperationException>(() => Engine.Replay(Prepared(), Input(wrongOrder))).Message.ShouldContain("NSE:ABB");
    }

    [Fact]
    public void One_adapter_prepares_two_strategies_that_do_not_share_state()
    {
        var a = Prepared(("or_min", 15));
        var b = Prepared(("or_min", 30));

        a.Record.DefinitionSha256.ShouldNotBe(b.Record.DefinitionSha256);
        a.ShouldNotBeSameAs(b);
    }

    private sealed class ForeignHandle : IPreparedStrategy
    {
        public StrategyRecord Record => throw new NotSupportedException();

        public IReadOnlyList<CandidateRecord> Candidates => throw new NotSupportedException();

        public bool HoldsOvernight => throw new NotSupportedException();

        public IReadOnlyList<FieldError> Warnings => [];
    }
}
