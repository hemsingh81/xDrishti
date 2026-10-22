using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>The post of a Lab run (P5-02 criteria 1, 2, 5): input checks, the window of the last year, the sample of thirty, the key and the idempotent insert, with fake ports.</summary>
public sealed class StartLabRunHandlerTests
{
    private static readonly string Hash = new('a', 64);

    private static readonly DateTimeOffset FirstBar = new(2024, 4, 1, 9, 15, 0, TimeSpan.FromHours(5.5));

    private static readonly DateTimeOffset LastBar = new(2026, 4, 30, 15, 29, 0, TimeSpan.FromHours(5.5));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();

    private readonly ITestLedger _ledger = Substitute.For<ITestLedger>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>> _resolver = Substitute.For<IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>>();

    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private readonly ISimulationEngine _engine = Substitute.For<ISimulationEngine>();

    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    private readonly NewLabRun[] _inserted = new NewLabRun[1];

    public StartLabRunHandlerTests()
    {
        _strategies.GetVersionAsync("LAB_TEST", Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new StoredVersion("LAB_TEST", 2, 1, Hash, "t", "{}", null, null, DateTimeOffset.UnixEpoch));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(214, 40_000_000, FirstBar, LastBar, 0, 0));
        _engine.EngineVersion.Returns("sim-5");
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<CostSheet>(Error.NotFound("costs.sheet_not_found", "none")));
        _runs.InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            _inserted[0] = ci.Arg<NewLabRun>();
            return new InsertedLabRun(ToRun(_inserted[0]), true);
        });
    }

    private static LabRun ToRun(NewLabRun n) => new(
        n.Id, n.Key, n.StrategyId, n.Version, n.StrategyHash, n.Universe, n.From, n.To, n.Method, n.ConfigHash, n.EngineVersion, LabRunState.Queued, null, 0, 0, [], null, [], [], n.Labels, null, false, n.CreatedAt, null, null);

    private StartLabRunHandler Handler() => new(_runs, _ledger, _strategies, _resolver, _baskets, _bars, _engine, _sheets, LabSettings.Default, new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 6, 0, 0, TimeSpan.Zero)), NullLogger<StartLabRunHandler>.Instance);

    private static ResolveResult Matched(int count) =>
        new([.. Enumerable.Range(1, count).Select(i => new ResolvedInstrument("S" + i, new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), "S" + i, "Name " + i, "EQUITY", null, false, true))], [], count, 0);

    private static StartLabRunCommand BySymbols(params string[] symbols) => new("LAB_TEST", null, null, symbols, "quick", null, null);

    private async Task<StartedLabRun> StartedAsync(int symbols)
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(symbols)));
        var result = await Handler().HandleAsync(BySymbols("S1"), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData(null, "quick", true, false, "strategyId")]
    [InlineData("LAB_TEST", "full", true, false, "method")]
    [InlineData("LAB_TEST", null, true, false, "method")]
    [InlineData("LAB_TEST", "quick", false, false, "exactly one")]
    [InlineData("LAB_TEST", "quick", true, true, "exactly one")]
    public async Task A_post_that_is_not_valid_is_refused_with_lab_invalid_input_and_nothing_is_stored(string? strategy, string? method, bool symbols, bool basket, string word)
    {
        var command = new StartLabRunCommand(strategy, null, basket ? Guid.NewGuid() : null, symbols ? ["S1"] : null, method, null, null);

        var result = await Handler().HandleAsync(command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        result.Error.Message.ShouldContain(word);
        await _runs.DidNotReceiveWithAnyArgs().InsertOrGetAsync(default!, Ct);
    }

    [Fact]
    public async Task An_empty_symbol_list_a_backwards_window_and_half_a_window_are_refused()
    {
        (await Handler().HandleAsync(BySymbols(), Ct)).Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        (await Handler().HandleAsync(BySymbols("S1") with { From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 1, 1) }, Ct)).Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        (await Handler().HandleAsync(BySymbols("S1") with { From = new DateOnly(2026, 2, 1) }, Ct)).Error.Code.ShouldBe(LabErrors.InvalidInputCode);
    }

    [Fact]
    public async Task An_unknown_strategy_or_version_is_not_found()
    {
        _strategies.GetVersionAsync("NOPE", Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns((StoredVersion?)null);

        var result = await Handler().HandleAsync(BySymbols("S1") with { StrategyId = "NOPE" }, Ct);

        result.Error.Code.ShouldBe(LabErrors.NotFoundCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task An_unknown_symbol_is_refused_by_name()
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new ResolveResult([], [new UnmatchedEntry("ZZZ", "no such instrument")], 1, 0)));

        var result = await Handler().HandleAsync(BySymbols("ZZZ"), Ct);

        result.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        result.Error.Message.ShouldContain("ZZZ");
    }

    [Fact]
    public async Task A_basket_that_is_missing_or_archived_is_not_found_and_an_empty_one_is_refused()
    {
        var id = Guid.NewGuid();
        _baskets.MembersAsync(id, null, null, Arg.Any<CancellationToken>()).Returns((BasketMembers?)null);
        (await Handler().HandleAsync(new StartLabRunCommand("LAB_TEST", null, id, null, "quick", null, null), Ct)).Error.Code.ShouldBe(LabErrors.NotFoundCode);

        _baskets.MembersAsync(id, null, null, Arg.Any<CancellationToken>()).Returns(new BasketMembers(id, "Empty", null, null, 0, []));
        (await Handler().HandleAsync(new StartLabRunCommand("LAB_TEST", null, id, null, "quick", null, null), Ct)).Error.Code.ShouldBe(LabErrors.InvalidInputCode);
    }

    [Fact]
    public async Task No_stored_bar_is_a_conflict_with_lab_no_data()
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(2)));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(0, 0, null, null, 0, 0));
        var empty = await Handler().HandleAsync(BySymbols("S1"), Ct);
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(214, 1, FirstBar, LastBar, 0, 0));
        var outside = await Handler().HandleAsync(BySymbols("S1") with { From = new DateOnly(2030, 1, 1), To = new DateOnly(2030, 2, 1) }, Ct);

        empty.Error.Code.ShouldBe(LabErrors.NoDataCode);
        empty.Error.Kind.ShouldBe(ErrorKind.Conflict);
        outside.Error.Code.ShouldBe(LabErrors.NoDataCode);
    }

    [Fact]
    public async Task The_window_is_the_last_year_of_stored_data_ending_at_the_last_stored_session()
    {
        var started = await StartedAsync(12);

        started.Run.Window.ShouldBe(new LabWindow(new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30)));
        started.Created.ShouldBeTrue();
        started.Run.State.ShouldBe("queued");
        started.Run.Verdict.ShouldBeNull();
    }

    [Fact]
    public async Task A_posted_window_is_kept_as_posted()
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(2)));

        var result = await Handler().HandleAsync(BySymbols("S1") with { From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 6, 30) }, Ct);

        result.Value.Run.Window.ShouldBe(new LabWindow(new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30)));
    }

    [Fact]
    public async Task Twelve_symbols_are_all_used_and_eighty_are_cut_to_the_same_thirty_every_time_with_the_seed_in_the_record()
    {
        var twelve = await StartedAsync(12);
        var first = await StartedAsync(80);
        var second = await StartedAsync(80);

        twelve.Run.Universe.Instruments.Count.ShouldBe(12);
        twelve.Run.Universe.Sampled.ShouldBeFalse();
        first.Run.Universe.Instruments.Count.ShouldBe(30);
        first.Run.Universe.Requested.ShouldBe(80);
        first.Run.Universe.Sampled.ShouldBeTrue();
        first.Run.Universe.Seed.ShouldNotBeNull();
        second.Run.Universe.Instruments.Select(i => i.Id).ShouldBe(first.Run.Universe.Instruments.Select(i => i.Id));
        second.Run.Key.ShouldBe(first.Run.Key);
        first.Run.Labels.ShouldContain(l => l.Contains("30 of 80", StringComparison.Ordinal) && l.Contains("seeded shuffle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_run_carries_the_exploration_line_and_the_gross_cost_basis_when_no_sheet_is_stored()
    {
        var started = await StartedAsync(3);

        started.Run.Labels.ShouldContain("Quick test: exploration, not a verdict.");
        started.Run.Labels.ShouldContain(l => l.StartsWith("Gross: no charges applied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_key_follows_the_strategy_hash_the_universe_the_window_and_the_engine()
    {
        var baseline = (await StartedAsync(5)).Run.Key;

        _engine.EngineVersion.Returns("sim-6");
        var otherEngine = (await StartedAsync(5)).Run.Key;
        _engine.EngineVersion.Returns("sim-5");
        _strategies.GetVersionAsync("LAB_TEST", Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new StoredVersion("LAB_TEST", 3, 1, new string('b', 64), "t", "{}", null, null, DateTimeOffset.UnixEpoch));
        var otherVersion = (await StartedAsync(5)).Run.Key;
        var otherUniverse = (await StartedAsync(6)).Run.Key;

        new[] { baseline, otherEngine, otherVersion, otherUniverse }.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task A_second_post_of_the_same_input_returns_the_run_that_holds_the_key_and_is_not_created()
    {
        var held = ToRun(new NewLabRun(Guid.NewGuid(), new string('k', 64), "LAB_TEST", 2, Hash, new LabUniverse("symbols", null, "l", 1, [], false, null), new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick", Hash, "sim-5", [], DateTimeOffset.UnixEpoch));
        _runs.InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>()).Returns(new InsertedLabRun(held, false));
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(2)));

        var result = await Handler().HandleAsync(BySymbols("S1"), Ct);

        result.Value.Created.ShouldBeFalse();
        result.Value.Run.Id.ShouldBe(held.Id);
    }

    [Fact]
    public async Task A_stored_sheet_in_force_on_the_first_day_makes_the_run_costed_and_changes_the_key_and_one_that_is_not_yet_in_force_leaves_it_gross()
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(3)));
        var window = BySymbols("S1") with { From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 4, 30) };
        var gross = (await Handler().HandleAsync(window, Ct)).Value.Run;
        _sheets.FindAsync(LabSettings.Default.CostSheetId, Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));

        var costed = (await Handler().HandleAsync(window, Ct)).Value.Run;
        var early = (await Handler().HandleAsync(window with { From = new DateOnly(2025, 6, 1) }, Ct)).Value.Run;

        costed.Labels.ShouldContain(l => l.StartsWith("Costed with the charge sheet", StringComparison.Ordinal));
        costed.Key.ShouldNotBe(gross.Key);
        early.Labels.ShouldContain(l => l.StartsWith("Gross:", StringComparison.Ordinal), "the sheet is not in force on the first day");
    }

    [Fact]
    public async Task A_new_run_appends_one_ledger_row_with_the_family_the_hash_and_the_parent()
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(2)));
        _strategies.GetVersionAsync("LAB_TEST", Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new StoredVersion("LAB_TEST", 2, 1, Hash, "t", "{}", new string('p', 64), null, DateTimeOffset.UnixEpoch));
        _ledger.AppendAsync(Arg.Any<LedgerRow>(), Arg.Any<CancellationToken>()).Returns(true);

        var started = (await Handler().HandleAsync(BySymbols("S1"), Ct)).Value;

        var row = _ledger.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<LedgerRow>();
        row.RunId.ShouldBe(started.Run.Id);
        row.StrategyId.ShouldBe("LAB_TEST");
        row.StrategyHash.ShouldBe(Hash);
        row.ParentHash.ShouldBe(new string('p', 64));
        row.RunKey.ShouldBe(started.Run.Key);
        row.Method.ShouldBe("quick");
    }

    [Fact]
    public async Task A_post_that_returns_a_held_run_appends_by_its_run_id_so_the_ledger_cannot_miss_it()
    {
        var held = ToRun(new NewLabRun(Guid.NewGuid(), new string('k', 64), "LAB_TEST", 2, Hash, new LabUniverse("symbols", null, "l", 1, [], false, null), new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick", Hash, "sim-5", [], DateTimeOffset.UnixEpoch));
        _runs.InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>()).Returns(new InsertedLabRun(held, false));
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(2)));

        await Handler().HandleAsync(BySymbols("S1"), Ct);

        await _ledger.Received(1).AppendAsync(Arg.Is<LedgerRow>(r => r.RunId == held.Id), Ct);
    }

    [Fact]
    public async Task A_refused_post_appends_nothing()
    {
        await Handler().HandleAsync(BySymbols(), Ct);

        await _ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
    }
}
