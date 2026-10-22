using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>Which strategy a run uses (spec P3-02d, D7 and D8, criteria 8, 9 and 11): a stored one by id (latest), id and version, or hash; a built-in id that is not stored; and what a refusal says.</summary>
public sealed class RunSimulationStoredStrategyTests
{
    private static readonly Guid Instrument = Guid.CreateVersion7();

    private static SimulationRequest Request(string id = "ORB", int? version = null, string? hash = null, Dictionary<string, double>? parameters = null) =>
        RunFixture.List([Instrument]) with { StrategyId = id, StrategyVersion = version, StrategyHash = hash, Parameters = parameters };

    private static RunFixture Fixture()
    {
        var fixture = new RunFixture();
        fixture.Add("AAA", RunFixture.ThreeDays(), id: Instrument);
        fixture.Strategies.Put("ORB", 1, "orb text one");
        fixture.Strategies.Put("ORB", 2, "orb text two");
        fixture.Strategies.Put("OTHER", 1, "other text");
        return fixture;
    }

    [Fact]
    public async Task A_stored_id_runs_its_latest_version_and_the_engine_is_given_the_text_and_the_hash()
    {
        var fixture = Fixture();

        var result = await fixture.RunAsync(Request(parameters: new() { ["rr"] = 3 }), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var stored = fixture.Engine.PreparedStored.ShouldHaveSingleItem();
        stored.SourceText.ShouldBe("orb text two");
        stored.ExpectedHash.ShouldBe((await fixture.Strategies.GetVersionAsync("ORB", 2, TestContext.Current.CancellationToken))!.Hash);
        stored.Parameters.ShouldBe(new Dictionary<string, double> { ["rr"] = 3 });
        fixture.Engine.Prepared.ShouldBeEmpty("the built-in path is not taken for a stored strategy");
    }

    [Fact]
    public async Task A_version_names_that_version()
    {
        var fixture = Fixture();

        (await fixture.RunAsync(Request(version: 1), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        fixture.Engine.PreparedStored.ShouldHaveSingleItem().SourceText.ShouldBe("orb text one");
    }

    [Fact]
    public async Task A_hash_names_a_version_by_content_and_the_id_may_be_left_out()
    {
        var fixture = Fixture();
        var hash = (await fixture.Strategies.GetVersionAsync("OTHER", 1, TestContext.Current.CancellationToken))!.Hash;

        (await fixture.RunAsync(Request(id: string.Empty, hash: hash), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        fixture.Engine.PreparedStored.ShouldHaveSingleItem().SourceText.ShouldBe("other text");
    }

    [Fact]
    public async Task A_hash_of_another_family_than_the_id_is_refused()
    {
        var fixture = Fixture();
        var hash = (await fixture.Strategies.GetVersionAsync("OTHER", 1, TestContext.Current.CancellationToken))!.Hash;

        var result = await fixture.RunAsync(Request(id: "ORB", hash: hash), TestContext.Current.CancellationToken);

        result.Error.Fields.ShouldNotBeNull().Single().Code.ShouldBe(SimulationErrors.UnknownStrategyCode);
        fixture.Engine.PreparedStored.ShouldBeEmpty();
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task An_unknown_version_is_refused_naming_the_versions_there_are_and_reads_nothing()
    {
        var fixture = Fixture();

        var result = await fixture.RunAsync(Request(version: 9), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(SimulationErrors.StrategyInvalidCode);
        var field = result.Error.Fields.ShouldNotBeNull().Single();
        (field.Field, field.Code).ShouldBe(("strategy_version", SimulationErrors.UnknownStrategyVersionCode));
        field.Message.ShouldContain("1 to 2");
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task An_unknown_hash_is_refused()
    {
        var fixture = Fixture();

        var result = await fixture.RunAsync(Request(id: string.Empty, hash: new string('0', 64)), TestContext.Current.CancellationToken);

        var field = result.Error.Fields.ShouldNotBeNull().Single();
        (field.Field, field.Code).ShouldBe(("strategy_hash", SimulationErrors.UnknownStrategyHashCode));
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task A_hash_and_a_version_together_are_a_request_error()
    {
        var fixture = Fixture();

        var result = await fixture.RunAsync(Request(version: 1, hash: new string('0', 64)), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(SimulationErrors.InvalidCode);
        result.Error.Fields.ShouldNotBeNull().ShouldContain(f => f.Field == "strategyVersion");
    }

    [Fact]
    public async Task An_id_that_is_not_stored_runs_the_built_in_of_the_engine()
    {
        var fixture = Fixture();

        var result = await fixture.RunAsync(Request(id: "VWAP_PULLBACK"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        fixture.Engine.Prepared.ShouldHaveSingleItem().StrategyId.ShouldBe("VWAP_PULLBACK");
        fixture.Engine.PreparedStored.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_id_is_refused_naming_the_stored_strategies_and_the_built_ins()
    {
        var fixture = Fixture();
        fixture.Engine.BuiltIns = [new BuiltInStrategyInfo("ZZZ_BUILT_IN", "x", [XDrishti.Domain.Market.Bars.Timeframe.M15], XDrishti.Domain.Market.Bars.Timeframe.M15, [])];
        fixture.Engine.PrepareResult = Result.Failure<XDrishti.Application.Abstractions.Research.IPreparedStrategy>(SimulationErrors.StrategyInvalid([new FieldError("strategy_id", "not built in", Code: SimulationErrors.UnknownStrategyCode)]));

        var result = await fixture.RunAsync(Request(id: "MACD"), TestContext.Current.CancellationToken);

        var field = result.Error.Fields.ShouldNotBeNull().Single();
        field.Code.ShouldBe(SimulationErrors.UnknownStrategyCode);
        field.Message.ShouldContain("`ORB`");
        field.Message.ShouldContain("`OTHER`");
        field.Message.ShouldContain("`ZZZ_BUILT_IN`");
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task A_version_of_an_id_that_is_not_stored_is_an_unknown_strategy()
    {
        var fixture = Fixture();

        var result = await fixture.RunAsync(Request(id: "MACD", version: 1), TestContext.Current.CancellationToken);

        result.Error.Fields.ShouldNotBeNull().Single().Code.ShouldBe(SimulationErrors.UnknownStrategyCode);
    }

    [Fact]
    public async Task What_the_engine_refuses_about_a_stored_text_passes_through_unchanged()
    {
        var fixture = Fixture();
        fixture.Engine.PrepareStoredResult = Result.Failure<XDrishti.Application.Abstractions.Research.IPreparedStrategy>(new Error("strategy_store.hash_mismatch", "The stored text does not give the hash.", ErrorKind.Conflict));

        var result = await fixture.RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("strategy_store.hash_mismatch");
        fixture.ShouldHaveReadNothing();
    }
}
