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
        n.Id, n.Key, n.StrategyId, n.Version, n.StrategyHash, n.Universe, n.From, n.To, n.Method, n.ConfigHash, n.EngineVersion, LabRunState.Queued, null, 0, 0, [], null, [], [], n.Labels, null, false, n.CreatedAt, null, null, n.RunOptions);

    private StartLabRunHandler Handler(XDrishti.Domain.Confidence.CostStressOptions? costStress = null) => new(_runs, _ledger, _strategies, _resolver, _baskets, _bars, _engine, _sheets, LabSettings.Default, new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 6, 0, 0, TimeSpan.Zero)), NullLogger<StartLabRunHandler>.Instance, costStress: costStress);

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
    [InlineData("LAB_TEST", "sensitivity", true, false, "method")]
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
    public async Task A_full_test_is_refused_without_a_stored_charge_sheet()
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(2)));

        var result = await Handler().HandleAsync(new StartLabRunCommand("LAB_TEST", null, null, ["S1"], "full", null, null), Ct);

        result.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        result.Error.Message.ShouldContain("costed");
        await _runs.DidNotReceiveWithAnyArgs().InsertOrGetAsync(default!, Ct);
    }

    [Fact]
    public async Task A_full_test_takes_the_whole_universe_has_its_own_key_and_says_what_it_is()
    {
        _sheets.FindAsync(LabSettings.Default.CostSheetId, Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(45)));
        var quick = (await Handler().HandleAsync(BySymbols("S1"), Ct)).Value.Run.Key;

        var full = (await Handler().HandleAsync(new StartLabRunCommand("LAB_TEST", null, null, ["S1"], "full", new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30)), Ct)).Value.Run;

        full.Method.ShouldBe("full");
        full.Key.ShouldNotBe(quick);
        full.Universe.Instruments.Count.ShouldBe(45);
        full.Universe.Sampled.ShouldBeFalse();
        full.Labels.ShouldContain(l => l.StartsWith("Full test: walk-forward out of sample", StringComparison.Ordinal));
        full.Labels.ShouldContain(l => l.Contains("FIXED_RR", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_robust_run_is_a_full_test_with_its_own_key_whole_universe_and_a_label_and_needs_a_sheet()
    {
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(45)));
        var refused = await Handler().HandleAsync(new StartLabRunCommand("LAB_TEST", null, null, ["S1"], "robust", null, null), Ct);
        refused.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        _sheets.FindAsync(LabSettings.Default.CostSheetId, Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        var window = (new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30));
        var full = (await Handler().HandleAsync(new StartLabRunCommand("LAB_TEST", null, null, ["S1"], "full", window.Item1, window.Item2), Ct)).Value.Run;

        var robust = (await Handler().HandleAsync(new StartLabRunCommand("LAB_TEST", null, null, ["S1"], "robust", window.Item1, window.Item2), Ct)).Value.Run;

        robust.Method.ShouldBe("robust");
        robust.Key.ShouldNotBe(full.Key);
        robust.Universe.Instruments.Count.ShouldBe(45);
        robust.Labels.ShouldContain(l => l.StartsWith("Robustness checks", StringComparison.Ordinal));
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

    private async Task<Result<StartedLabRun>> Post(string method, string? slippage = null, bool? stress = null, XDrishti.Domain.Confidence.CostStressOptions? costStress = null)
    {
        _sheets.FindAsync(LabSettings.Default.CostSheetId, Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(2)));
        var window = (new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30));
        return await Handler(costStress).HandleAsync(new StartLabRunCommand("LAB_TEST", null, null, ["S1"], method, window.Item1, window.Item2, slippage, stress), Ct);
    }

    [Fact]
    public async Task Tier_settings_that_differ_from_their_defaults_change_the_key_of_a_conservative_run_only()
    {
        var wide = new XDrishti.Domain.Confidence.CostStressOptions() with { TierABps = 4m };
        var defaults = new XDrishti.Domain.Confidence.CostStressOptions();

        var conservative = (await Post("robust", "conservative", null, defaults)).Value.Run.Key;
        (await Post("robust", "conservative", null, wide)).Value.Run.Key.ShouldNotBe(conservative, "another floor is another run");
        (await Post("robust", "conservative", null, defaults with { RangeFraction = 0.15 })).Value.Run.Key.ShouldNotBe(conservative);
        (await Post("robust", null, null, wide)).Value.Run.Key.ShouldBe((await Post("robust", null, null, defaults)).Value.Run.Key, "the floors are not used without the option");
    }

    [Fact]
    public async Task The_threshold_of_min_turnover_changes_the_key_only_for_a_strategy_whose_parsed_definition_names_the_filter()
    {
        var description = new StrategyDescription("LAB_TEST", "intraday", Hash, "{}", [], [], [], true);
        var defaults = new XDrishti.Domain.Confidence.CostStressOptions();

        _engine.Describe(Arg.Any<string>()).Returns(Result.Success(description));
        var named20 = (await Post("robust", null, null, defaults)).Value.Run.Key;
        var named30 = (await Post("robust", null, null, defaults with { MinTurnoverCrore = 30m })).Value.Run.Key;
        _engine.Describe(Arg.Any<string>()).Returns(Result.Success(description with { ReadsTurnover = false }));
        var plain20 = (await Post("robust", null, null, defaults)).Value.Run.Key;
        var plain30 = (await Post("robust", null, null, defaults with { MinTurnoverCrore = 30m })).Value.Run.Key;

        named30.ShouldNotBe(named20, "another threshold is another run");
        plain30.ShouldBe(plain20, "a text that only mentions the filter in a comment, or not at all, does not read the setting");
        named20.ShouldNotBe(plain20);
    }

    [Fact]
    public async Task The_default_options_keep_every_key_and_a_spelled_default_is_the_same_run()
    {
        var plain = (await Post("robust")).Value.Run.Key;

        (await Post("robust", "default", true)).Value.Run.Key.ShouldBe(plain, "stress defaults to on for robust and slippage to default");
        (await Post("full", "default", false)).Value.Run.Key.ShouldBe((await Post("full")).Value.Run.Key);
    }

    [Fact]
    public async Task Conservative_slippage_and_a_non_default_stress_are_other_runs_that_remember_their_options()
    {
        var plain = (await Post("robust")).Value.Run.Key;
        var plainOptions = _inserted[0].RunOptions;
        var conservative = (await Post("robust", "conservative")).Value.Run.Key;
        var conservativeOptions = _inserted[0].RunOptions;
        var noStress = (await Post("robust", null, false)).Value.Run.Key;
        var noStressOptions = _inserted[0].RunOptions;
        var fullPlain = (await Post("full")).Value.Run.Key;
        var fullStress = (await Post("full", null, true)).Value.Run.Key;
        var fullStressOptions = _inserted[0].RunOptions;

        conservative.ShouldNotBe(plain);
        noStress.ShouldNotBe(plain);
        fullStress.ShouldNotBe(fullPlain);
        plainOptions.ShouldBeNull();
        conservativeOptions.ShouldBe("slippage=conservative");
        noStressOptions.ShouldBe("stress=off");
        fullStressOptions.ShouldBe("stress=on");
    }

    [Fact]
    public async Task An_unknown_slippage_and_a_stress_on_a_quick_run_are_refused()
    {
        (await Post("robust", "aggressive")).Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        (await Post("quick", null, true)).Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        (await Post("quick", "conservative")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Stress_off_on_a_robust_run_is_refused_while_the_cost_stress_is_enforced_and_allowed_otherwise()
    {
        var enforced = new XDrishti.Domain.Confidence.CostStressOptions(Enforce: true);

        var refused = await Post("robust", null, false, enforced);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        refused.Error.Message.ShouldContain("enforced");
        (await Post("robust", null, false, new XDrishti.Domain.Confidence.CostStressOptions())).IsSuccess.ShouldBeTrue("report only: nothing is capped");
        (await Post("robust", null, true, enforced)).IsSuccess.ShouldBeTrue();
        (await Post("robust", null, null, enforced)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_options_are_said_in_the_labels_of_the_run_and_a_run_without_them_has_neither_line()
    {
        await Post("robust");
        var plain = _inserted[0].Labels;
        await Post("robust", "conservative", false);
        var optioned = _inserted[0].Labels;

        plain.ShouldNotContain(RunLabels.ConservativeSlippage);
        plain.ShouldNotContain(RunLabels.StressOff);
        optioned.ShouldContain("slippage: conservative (tier C and unavailable excluded)");
        optioned.ShouldContain("stress: off");
    }

    [Fact]
    public async Task The_served_run_says_its_options_and_whether_they_are_the_defaults()
    {
        var plain = (await Post("robust")).Value.Run.Options.ShouldNotBeNull();
        var conservative = (await Post("robust", "conservative")).Value.Run.Options.ShouldNotBeNull();
        var stressedFull = (await Post("full", null, true)).Value.Run.Options.ShouldNotBeNull();

        plain.ShouldBe(new LabRunOptionsView("default", true, true));
        conservative.ShouldBe(new LabRunOptionsView("conservative", true, false));
        stressedFull.ShouldBe(new LabRunOptionsView("default", true, false));
    }
}
