using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>Criteria 1 and 2 of P3-04a: an invalid request and a refused strategy fail before any port is read, and the engine is not asked to replay anything.</summary>
public sealed class RunSimulationRefusalTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-0000000000a1");

    private static SimulationRequest Valid => RunFixture.List([Id]);

    private static readonly Dictionary<string, (Func<SimulationRequest, SimulationRequest> Change, string Field)> Cases = new(StringComparer.Ordinal)
    {
        ["empty strategy id"] = (r => r with { StrategyId = " " }, "strategyId"),
        ["unparsable timeframe"] = (r => r with { EvaluationTimeframe = "7m" }, "evaluationTimeframe"),
        ["no timeframe"] = (r => r with { EvaluationTimeframe = null! }, "evaluationTimeframe"),
        ["from after to"] = (r => r with { From = new DateOnly(2026, 3, 5), To = new DateOnly(2026, 3, 4) }, "from"),
        ["from in 1999"] = (r => r with { From = new DateOnly(1999, 12, 31) }, "from"),
        ["to in 2100"] = (r => r with { To = new DateOnly(2100, 1, 1) }, "to"),
        ["both universes"] = (r => r with { BasketId = Guid.NewGuid() }, "basketId"),
        ["no universe"] = (r => r with { InstrumentIds = null }, "instrumentIds"),
        ["no ids"] = (r => r with { InstrumentIds = [] }, "instrumentIds"),
        ["2001 ids"] = (r => r with { InstrumentIds = [.. Enumerable.Range(0, 2_001).Select(i => new Guid(i, 0, 0, new byte[8]))] }, "instrumentIds"),
        ["a repeated id"] = (r => r with { InstrumentIds = [Id, Id] }, "instrumentIds"),
        ["completeness 0"] = (r => r with { Options = new ReplayOptions(0, 0.2) }, "options.minSessionCompleteness"),
        ["completeness above 1"] = (r => r with { Options = new ReplayOptions(1.01, 0.2) }, "options.minSessionCompleteness"),
        ["completeness NaN"] = (r => r with { Options = new ReplayOptions(double.NaN, 0.2) }, "options.minSessionCompleteness"),
        ["threshold 0"] = (r => r with { Options = new ReplayOptions(0.9, 0) }, "options.discontinuityThreshold"),
        ["threshold infinite"] = (r => r with { Options = new ReplayOptions(0.9, double.PositiveInfinity) }, "options.discontinuityThreshold"),
        ["parameter NaN"] = (r => r with { Parameters = new Dictionary<string, double> { ["or_min"] = double.NaN } }, "parameters.or_min"),
        ["parameter infinite"] = (r => r with { Parameters = new Dictionary<string, double> { ["rr"] = double.NegativeInfinity } }, "parameters.rr"),
        ["fill limit NaN"] = (r => r with { Fills = new FillRulesInput(double.NaN, 1, 0.05, 2) }, "fills.entryLimitPercent"),
        ["fill ticks infinite"] = (r => r with { Fills = new FillRulesInput(0.1, double.PositiveInfinity, 0.05, 2) }, "fills.ticks"),
        ["fill range fraction NaN"] = (r => r with { Fills = new FillRulesInput(0.1, 1, double.NaN, 2) }, "fills.barRangeFrac"),
        ["fill extra ticks infinite"] = (r => r with { Fills = new FillRulesInput(0.1, 1, 0.05, double.NegativeInfinity) }, "fills.stopExitExtraTicks"),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task An_invalid_request_is_refused_naming_the_field_and_nothing_is_read(string name)
    {
        var (change, field) = Cases[name];
        var fixture = new RunFixture();

        var result = await fixture.RunAsync(change(Valid), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SimulationErrors.InvalidCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldContain(field);
        fixture.Engine.Prepared.ShouldBeEmpty("the strategy is prepared only for a valid request");
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task Every_wrong_field_is_named_at_once()
    {
        var fixture = new RunFixture();
        var request = Valid with { StrategyId = string.Empty, EvaluationTimeframe = "x", From = new DateOnly(2026, 3, 9), To = new DateOnly(2026, 3, 2) };

        var result = await fixture.RunAsync(request, TestContext.Current.CancellationToken);

        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["strategyId", "evaluationTimeframe", "from"]);
    }

    [Theory]
    [InlineData(2000, 1, 1)]
    [InlineData(2099, 12, 31)]
    public async Task The_first_and_last_day_of_the_importers_range_are_accepted(int year, int month, int day)
    {
        var fixture = new RunFixture();
        var id = fixture.Add("NSE:ABB");
        var date = new DateOnly(year, month, day);

        var result = await fixture.RunAsync(RunFixture.List([id], date, date), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_refused_strategy_is_refused_before_the_basket_the_calendar_or_any_bar_is_read()
    {
        var fixture = new RunFixture();
        fixture.Engine.PrepareResult = Result.Failure<XDrishti.Application.Abstractions.Research.IPreparedStrategy>(SimulationErrors.StrategyInvalid(
        [
            new FieldError("strategy_id", "not built in", Code: SimulationErrors.UnknownStrategyCode),
            new FieldError("buy.trigger[0]", "reads a market context", Code: "strategy.lint.context"),
        ]));

        var result = await fixture.RunAsync(RunFixture.Basket(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(SimulationErrors.StrategyInvalidCode);
        result.Error.Fields.ShouldNotBeNull().Select(f => (f.Field, f.Code)).ShouldBe([("strategy_id", SimulationErrors.UnknownStrategyCode), ("buy.trigger[0]", "strategy.lint.context")]);
        fixture.Engine.Prepared.Count.ShouldBe(1);
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task More_than_twenty_problems_are_listed_twenty_and_the_rest_are_counted_in_the_message()
    {
        var fixture = new RunFixture();
        var problems = Enumerable.Range(0, 25).Select(i => new FieldError("parameters.p" + i, "unknown", Code: SimulationErrors.UnknownParameterCode)).ToList();
        fixture.Engine.PrepareResult = Result.Failure<XDrishti.Application.Abstractions.Research.IPreparedStrategy>(Error.Validation(SimulationErrors.StrategyInvalidCode, "refused") with { Fields = problems });

        var result = await fixture.RunAsync(Valid, TestContext.Current.CancellationToken);

        result.Error.Fields.ShouldNotBeNull().Count.ShouldBe(20);
        result.Error.Message.ShouldContain("25");
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task The_request_is_passed_to_the_engine_as_the_effective_strategy_parameters_timeframe_and_fills()
    {
        var fixture = new RunFixture();
        var id = fixture.Add("NSE:ABB");
        var request = Valid with { InstrumentIds = [id], Parameters = new Dictionary<string, double> { ["or_min"] = 30 }, Fills = new FillRulesInput(0.1, 0, 0, 0) };

        var result = await fixture.RunAsync(request, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var (strategyId, parameters, timeframe, fills) = fixture.Engine.Prepared.Single();
        strategyId.ShouldBe("ORB");
        parameters["or_min"].ShouldBe(30);
        timeframe.ShouldBe(XDrishti.Domain.Market.Bars.Timeframe.M15);
        fills.ShouldNotBeNull().Slippage.IsNone.ShouldBeTrue();
        result.Value.Fills.ShouldBe(fills.ToCanonicalJson());
    }

    [Fact]
    public async Task A_request_without_fills_leaves_the_rules_to_the_engine_and_records_the_default()
    {
        var fixture = new RunFixture();
        var id = fixture.Add("NSE:ABB");

        var result = await fixture.RunAsync(RunFixture.List([id]), TestContext.Current.CancellationToken);

        fixture.Engine.Prepared.Single().Fills.ShouldBeNull();
        result.Value.Fills.ShouldBe(FillRules.Default.ToCanonicalJson());
    }

    [Theory]
    [InlineData(6, 1, 0.05, 2, SimulationErrors.FillRulesInvalidCode)]
    [InlineData(-0.1, 1, 0.05, 2, SimulationErrors.FillRulesInvalidCode)]
    [InlineData(0.1, 101, 0.05, 2, SimulationErrors.SlippageInvalidCode)]
    [InlineData(0.1, 1, 1.5, 2, SimulationErrors.SlippageInvalidCode)]
    [InlineData(0.1, 1, 0.05, -1, SimulationErrors.SlippageInvalidCode)]
    public async Task Fill_rules_out_of_range_are_refused_with_the_codes_of_the_fill_model_and_nothing_is_read(double limit, double ticks, double range, double extra, string code)
    {
        var fixture = new RunFixture();

        var result = await fixture.RunAsync(Valid with { Fills = new FillRulesInput(limit, ticks, range, extra) }, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(code);
        fixture.Engine.Prepared.ShouldBeEmpty();
        fixture.ShouldHaveReadNothing();
    }
}
