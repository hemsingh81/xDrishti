using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Research;

/// <summary>Criterion 9 of P3-10 (validation before the database, a fake store with a call count of 0 in each case) and the outcome mapping and log lines of D8 and D15.</summary>
public sealed class StoreRunHandlerTests
{
    private const string Json = "{\"schema_version\":5}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Hex = new('a', 64);

    private static RunRecordText Record(string json = Json) => new(json, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))), Hex);

    private static SimTrade Costed(SimTrade trade, string sheetId)
    {
        var applied = TradeCostApplier.Apply(trade, FixtureCosts(sheetId), CostSegment.IntradayEquity);
        return applied.Value.Trade!;
    }

    private static CostContext FixtureCosts(string sheetId) => TestCostSheet.Context(sheet: FixtureSheet.Build(sheetId, null));

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = Substitute.For<IRunStore>();
            Store.StoreAsync(Arg.Any<RunToStore>(), Arg.Any<CancellationToken>()).Returns(Result.Success(StoreOutcome.Stored(7, 12)));
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 5, 0, 0, TimeSpan.Zero));
            Log = new StructuredLogger<StoreRunHandler>();
            Handler = new StoreRunHandler(Store, Time, Log);
        }

        public IRunStore Store { get; }

        public FakeTimeProvider Time { get; }

        public StructuredLogger<StoreRunHandler> Log { get; }

        public StoreRunHandler Handler { get; }

        public ValueTask<Result<StoreOutcome>> RunAsync(SimulationRun? run, RunRecordText? record = null) => Handler.HandleAsync(new StoreRunCommand(run, record ?? Record(), "cli:simulate"), Ct);

        public async Task StoreWasNotCalled() => await Store.DidNotReceiveWithAnyArgs().StoreAsync(default!, default);
    }

    private static async Task<Error> RefusedAsync(SimulationRun? run, RunRecordText? record = null)
    {
        var fixture = new Fixture();

        var result = await fixture.RunAsync(run, record);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("store.run_invalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        await fixture.StoreWasNotCalled();
        return result.Error;
    }

    [Fact]
    public async Task A_valid_run_is_given_to_the_store_with_the_actor_and_the_instant_read_once()
    {
        var fixture = new Fixture();
        var run = FabricatedRun.Standard();

        var result = await fixture.RunAsync(run);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(7);
        result.Value.WasAlreadyStored.ShouldBeFalse();
        await fixture.Store.Received(1).StoreAsync(Arg.Is<RunToStore>(s => s.Run == run && s.Actor == "cli:simulate" && s.CreatedAt == fixture.Time.GetUtcNow()), Ct);
    }

    [Fact]
    public async Task A_null_run_is_refused_before_the_store()
    {
        var error = await RefusedAsync(null);

        error.Fields!.Select(f => f.Field).ShouldBe(["run"]);
    }

    [Fact]
    public async Task A_run_key_that_is_not_64_lower_case_hex_is_refused()
    {
        var error = await RefusedAsync(FabricatedRun.Standard() with { RunKey = new string('A', 64) });

        error.Fields!.Select(f => f.Field).ShouldContain("run.runKey");
    }

    [Fact]
    public async Task A_run_of_record_schema_5_is_refused()
    {
        var error = await RefusedAsync(FabricatedRun.Standard() with { SchemaVersion = 5 });

        error.Fields!.Select(f => f.Field).ShouldContain("run.schemaVersion");
    }

    [Fact]
    public async Task A_trade_of_schema_3_is_refused()
    {
        var run = FabricatedRun.Standard();
        var old = run.Trades[0] with { Trade = run.Trades[0].Trade with { SchemaVersion = 3 } };

        var error = await RefusedAsync(run with { Trades = [old, .. run.Trades.Skip(1)] });

        error.Fields!.Select(f => f.Field).ShouldContain("run.trades.schemaVersion");
    }

    private static ValueList<Leg> TwoLegsAndAnExit(SimTrade first, string reason = "X") => new(
    [
        new Leg(first.EntryEpochSeconds, first.EntryPrice, first.EntryFilledPrice, 1m, LegKind.Entry, string.Empty),
        new Leg(first.ExitEpochSeconds, first.ExitPrice, first.ExitFilledPrice, 0.5m, LegKind.Exit, reason),
        new Leg(first.ExitEpochSeconds, first.ExitPrice, first.ExitFilledPrice, 0.5m, LegKind.Exit, "Y"),
    ]);

    [Fact]
    public async Task A_trade_with_legs_is_stored_the_legs_have_their_own_tables_since_P4_05()
    {
        var run = FabricatedRun.Standard();
        var first = run.Trades[0].Trade;
        var withLegs = run.Trades[0] with { Trade = first with { Legs = TwoLegsAndAnExit(first) } };
        var fixture = new Fixture();

        var stored = await fixture.RunAsync(run with { Trades = [withLegs, .. run.Trades.Skip(1)] });

        stored.IsSuccess.ShouldBeTrue(stored.IsFailure ? stored.Error.Message : string.Empty);
        await fixture.Store.Received(1).StoreAsync(Arg.Is<RunToStore>(r => r.Run.Trades[0].Trade.Legs!.Count == 3), Ct);
    }

    [Fact]
    public async Task Legs_the_tables_cannot_hold_are_refused_by_name()
    {
        var run = FabricatedRun.Standard();
        var first = run.Trades[0].Trade;
        var oneLeg = first with { Legs = new ValueList<Leg>([new Leg(first.EntryEpochSeconds, first.EntryPrice, first.EntryFilledPrice, 1m, LegKind.Entry, string.Empty)]) };
        var longReason = first with { Legs = TwoLegsAndAnExit(first, new string('r', 65)) };

        foreach (var trade in new[] { oneLeg, longReason })
        {
            var error = await RefusedAsync(run with { Trades = [run.Trades[0] with { Trade = trade }, .. run.Trades.Skip(1)] });

            error.Fields!.Select(f => f.Field).ShouldContain("run.trades.legs");
        }
    }

    [Fact]
    public async Task A_record_text_whose_hash_is_not_the_digest_given_is_refused()
    {
        var error = await RefusedAsync(FabricatedRun.Standard(), new RunRecordText(Json, Hex, Hex));

        error.Fields!.Select(f => f.Field).ShouldContain("record.recordSha256");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_digest_that_is_not_64_lower_case_hex_cannot_be_made(string figure)
    {
        Should.Throw<ArgumentException>(() => new RunRecordText(Json, figure, Hex));
        Should.Throw<ArgumentException>(() => new RunRecordText(Json, Hex, figure));
    }

    [Fact]
    public async Task Lists_that_hold_more_trades_than_the_record_counted_are_refused()
    {
        var run = FabricatedRun.Standard();

        var error = await RefusedAsync(run with { Totals = run.Totals with { TradesInWindow = 0 } });

        error.Fields!.Select(f => f.Field).ShouldContain("run.trades");
    }

    [Fact]
    public async Task Two_trades_with_one_trade_key_are_refused()
    {
        var run = FabricatedRun.Standard();

        var error = await RefusedAsync(run with { Trades = [run.Trades[0], run.Trades[0]] });

        error.Fields!.Select(f => f.Field).ShouldContain("run.trades.key");
    }

    [Fact]
    public async Task A_costed_trade_in_a_gross_run_is_refused()
    {
        var run = FabricatedRun.Standard();
        var costed = run.Trades[0] with { Trade = Costed(run.Trades[0].Trade, FixtureSheet.Id) };

        var error = await RefusedAsync(run with { Trades = [costed, .. run.Trades.Skip(1)] });

        error.Fields!.Select(f => f.Field).ShouldContain("run.trades.costing");
    }

    [Fact]
    public async Task A_gross_trade_in_a_costed_run_is_refused()
    {
        var run = FabricatedRun.Standard() with { Costs = CostRecord.Of(FixtureCosts(FixtureSheet.Id)) };

        var error = await RefusedAsync(run);

        error.Fields!.Select(f => f.Field).ShouldContain("run.trades.costing");
    }

    [Fact]
    public async Task A_costing_whose_sheet_id_is_not_the_runs_is_refused()
    {
        var run = FabricatedRun.Standard();
        var other = run.Trades.Select(t => t with { Trade = Costed(t.Trade, "fixture-b") }).ToList();
        var openOther = run.OpenAtEnd.Select(t => t with { Trade = Costed(t.Trade, "fixture-b") }).ToList();

        var error = await RefusedAsync(run with { Trades = other, OpenAtEnd = openOther, Costs = CostRecord.Of(FixtureCosts(FixtureSheet.Id)) });

        error.Fields!.Select(f => f.Field).ShouldContain("run.trades.costing.sheetId");
    }

    [Fact]
    public async Task A_run_costed_by_a_test_sheet_is_refused()
    {
        var error = await RefusedAsync(FabricatedRun.CostedStandard());

        error.Fields!.Select(f => f.Field).ShouldContain("run.costs.sheetId");
    }

    [Fact]
    public async Task All_problems_are_listed_at_most_twenty()
    {
        var run = FabricatedRun.Standard() with { RunKey = "x", SchemaVersion = 4 };

        var error = await RefusedAsync(run, new RunRecordText(Json, Hex, Hex));

        error.Fields!.Select(f => f.Field).ShouldBe(["record.recordSha256", "run.runKey", "run.schemaVersion"], ignoreOrder: true);
        error.Fields!.Count.ShouldBeLessThanOrEqualTo(StoreRunValidation.MaxProblems);
    }

    [Fact]
    public async Task An_already_stored_run_is_a_success_and_is_logged_as_such()
    {
        var fixture = new Fixture();
        fixture.Store.StoreAsync(Arg.Any<RunToStore>(), Arg.Any<CancellationToken>()).Returns(Result.Success(StoreOutcome.AlreadyStored(3)));

        var result = await fixture.RunAsync(FabricatedRun.Standard());

        result.Value.WasAlreadyStored.ShouldBeTrue();
        fixture.Log.Events.Select(e => e.Id).ShouldBe([4415, 4416]);
        fixture.Log.Events[1].Argument("Outcome").ShouldBe("already stored");
    }

    [Fact]
    public async Task A_conflict_is_returned_and_logged_once_at_error_with_the_names_of_the_figures_and_no_value()
    {
        var fixture = new Fixture();
        var run = FabricatedRun.Standard();
        fixture.Store.StoreAsync(Arg.Any<RunToStore>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<StoreOutcome>(StoreErrors.RunConflict(run.RunKey, ["record", "trades"])));

        var result = await fixture.RunAsync(run);

        result.Error.Code.ShouldBe("store.run_conflict");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        fixture.Log.Events.Select(e => e.Id).ShouldBe([4415, 4417]);
        fixture.Log.Events[1].Level.ShouldBe(LogLevel.Error);
        fixture.Log.Events[1].Argument("Differing").ShouldBe("record, trades");
        fixture.Log.Events[1].Argument("RunKey").ShouldBe(run.RunKey[..12]);
    }

    [Fact]
    public async Task A_failed_write_is_returned_and_logged_with_its_code_only()
    {
        var fixture = new Fixture();
        fixture.Store.StoreAsync(Arg.Any<RunToStore>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("rolled back")));

        var result = await fixture.RunAsync(FabricatedRun.Standard());

        result.Error.Code.ShouldBe("store.write_failed");
        fixture.Log.Events.Select(e => e.Id).ShouldBe([4415, 4418]);
        fixture.Log.Events[1].Argument("Code").ShouldBe("store.write_failed");
    }

    [Fact]
    public async Task An_unexpected_exception_is_logged_by_code_and_raised_again()
    {
        var fixture = new Fixture();
        fixture.Store.StoreAsync(Arg.Any<RunToStore>(), Arg.Any<CancellationToken>()).Returns<Result<StoreOutcome>>(_ => throw new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await fixture.RunAsync(FabricatedRun.Standard()));

        fixture.Log.Events.Select(e => e.Id).ShouldBe([4415, 4418]);
    }

    [Fact]
    public async Task No_log_line_holds_the_record_text_or_a_symbol()
    {
        var fixture = new Fixture();
        var run = FabricatedRun.Standard();

        await fixture.RunAsync(run);

        fixture.Log.Events.ShouldAllBe(e => !e.Message.Contains("schema_version", StringComparison.Ordinal) && !e.Message.Contains("AAA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Texts_longer_than_their_columns_and_digests_that_are_not_64_hex_are_refused_before_the_database()
    {
        var run = FabricatedRun.Standard();
        var long1 = run.Trades[0] with { Trade = run.Trades[0].Trade with { Symbol = new string('x', 41) } };
        var bad = run with
        {
            Trades = [long1, .. run.Trades.Skip(1)],
            Strategy = run.Strategy with { Id = new string('i', 65), DefinitionSha256 = "abc" },
            Universe = run.Universe with { Label = new string('l', 201) },
            Fills = new string('f', 2001),
        };

        var error = await RefusedAsync(bad);

        error.Fields!.Select(f => f.Field).ShouldBe(["run.strategy.id", "run.strategy.definitionSha256", "run.universe.label", "run.fills", "run.trades.symbol"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_costs_rates_hash_that_is_not_64_hex_is_refused()
    {
        var run = FabricatedRun.Standard() with { Costs = CostRecord.Of(FixtureCosts(FixtureSheet.Id)) with { RatesSha256 = "short" } };

        var error = await RefusedAsync(run);

        error.Fields!.Select(f => f.Field).ShouldContain("run.costs.ratesSha256");
    }
}
