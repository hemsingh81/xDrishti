#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Lab;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.HoldOut;
using XDrishti.Application.Tests.Lab;
using XDrishti.Application.Tests.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Domain.Selection.SmartRules;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay.HoldOut;

/// <summary>SR-03 (ADR 0284): the registration and its refusals, the one claim of the system look and its refusals (drift, filling, used, superseded), the system slot and the auto step.</summary>
public sealed class SystemReplayHoldOutTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly string VersionHash = new('3', 64);
    private static readonly DateOnly One = new(2026, 5, 1);
    private static readonly DateOnly OneEnd = new(2026, 11, 4);
    private static readonly DateOnly Two = new(2026, 11, 5);
    private static readonly DateOnly TwoEnd = new(2027, 5, 4);
    private static readonly DateOnly Three = new(2027, 5, 5);
    private static readonly DateOnly ThreeEnd = new(2027, 11, 4);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly MemoryHoldOutStore _blocks = new();
    private readonly MemoryRegistrationStore _regs = new();
    private readonly FakeCostSheetStore _sheets = new();
    private readonly IReplayFacts _facts = Substitute.For<IReplayFacts>();
    private readonly IReplayInputs _inputs = Substitute.For<IReplayInputs>();
    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();
    private readonly IAutoScopeStore _autoScope = Substitute.For<IAutoScopeStore>();
    private readonly IRiskProfileStore _profiles = Substitute.For<IRiskProfileStore>();
    private readonly ISimulationEngineVersion _engine = Substitute.For<ISimulationEngineVersion>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 4, 20, 6, 0, 0, TimeSpan.Zero));
    private int _members = 2;

    public SystemReplayHoldOutTests()
    {
        _blocks.Blocks.Add(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UnixEpoch));
        _blocks.Blocks.Add(new HoldOutBlock(Two, new DateOnly(2024, 10, 1), TwoEnd, 6, Hash, DateTimeOffset.UnixEpoch, One));
        _blocks.Releases.Add(new StoredRelease(One, DateTimeOffset.UnixEpoch, "owner", "owner:x", Hash, true, Two));
        _sheets.With(Sheet("dhan-2026-04"));
        _engine.EngineVersion.Returns("sim-5");
        _profiles.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<XDrishti.Domain.Accounts.RiskProfile>>([]));
        _facts.SettingsAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Snapshot()));
        _facts.UniverseAsync(Arg.Any<ReplayParams>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(new ReplayUniverse([.. Enumerable.Range(0, _members).Select(i => new ReplayInstrument(new Guid(i + 1, 0, 0, new byte[8]), "S" + i))], false)));
        _facts.CellsAsync(Arg.Any<ReplayParams>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(c => ValueTask.FromResult(Cells(c.ArgAt<DateOnly>(1))));
        _inputs.VersionsAsync(Arg.Any<ReplayParams>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>>>(new Dictionary<string, IReadOnlyList<StoredVersion>> { ["S1"] = [Version()] }));
        _strategies.ListVersionsAsync("S1", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredVersion>>([Version()]));
    }

    private static StoredVersion Version() => new("S1", 1, 1, VersionHash, "text", "{}", null, null, DateTimeOffset.UnixEpoch);

    private static CostSheet Sheet(string id, bool checkedNote = true, decimal brokerage = 0.03m) =>
        CostSheet.Create(id, "sheet", "contract note", checkedNote ? "checked against contract note 2026-04" : null, TestCostSheet.Of(id, TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.Rates(brokerage, 20m, 0m, 0m, 0.04m, 0.005m, 50m, 0.02m, 0m, 0m), TestCostSheet.DeliveryA)).Schedules).Value;

    private static ReplaySettingsSnapshot Snapshot() =>
        new(new SelectionRules(), new SmartRulesOptions(), new FillConstraints(), new BasketSettingsState(BasketSettings.Defaults, 1), new SizingState(SizingSettings.Defaults, 0), new Dictionary<Guid, double>(), 0.2);

    private static ReplayCells Cells(DateOnly cut)
    {
        var cell = new CellId("S1", "1D", "BUY", "swing", "FIXED_RR{rr=2}", Expression: Expression.Cash);
        var stored = new StoredCellStats(new string('1', 64), DateTimeOffset.UnixEpoch, new CellStatsOptions(), "run:swing", new CellStatsRow(cell, CellWindow.All, cut, true, 40, 20, 40, 0.5, 0.5, 0.55, 0.50, 0.6, 2.0, -1.0, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true));
        return new ReplayCells([new ReplayCell(stored, null, null)], []);
    }

    private HoldOutEpochs Epochs() => new(_blocks, new FixedCalendar(), _time);

    private HoldOutInputCheck Check() => new(_facts, _strategies, _sheets);

    private RegisterHoldOutHandler Register(HoldOutOptions? options = null) =>
        new(_blocks, _regs, _facts, _inputs, _sheets, _autoScope, _profiles, _engine, new SystemReplayOptions(), options ?? new HoldOutOptions(), _time);

    private RunHoldOutRegistrationHandler Run() => new(_blocks, _regs, Epochs(), Check(), new HoldOutOptions(), _time);

    private static RegisterHoldOutCommand Command(string? sheet = "dhan-2026-04", string mode = "owner", double? limit = null) =>
        new(null, [new Guid(1, 0, 0, new byte[8])], null, null, sheet, limit, mode, "owner:x");

    /// <summary>Block 3 is in force (block 2 released) and its last session has closed: the registration made on 2027-04-20 can be run.</summary>
    private async Task<Guid> SealedBlockThreeWithRegistrationAsync(bool sealedNow = true)
    {
        _blocks.Releases.Add(new StoredRelease(Two, DateTimeOffset.UnixEpoch, "owner", "owner:x", Hash, true, Three));
        _blocks.Blocks.Add(new HoldOutBlock(Three, new DateOnly(2024, 10, 1), ThreeEnd, 6, Hash, DateTimeOffset.UnixEpoch, Two));
        var made = await Register().HandleAsync(Command(), Ct);
        made.IsSuccess.ShouldBeTrue(made.IsFailure ? made.Error.Message : null);
        _time.SetUtcNow(sealedNow ? new DateTimeOffset(2027, 11, 5, 6, 0, 0, TimeSpan.Zero) : new DateTimeOffset(2027, 10, 1, 6, 0, 0, TimeSpan.Zero));
        return made.Value.Id;
    }

    // ---- register (AC 1, 2)

    [Fact]
    public async Task A_registration_for_the_next_block_stores_the_frozen_inputs_and_the_same_inputs_give_the_same_hash()
    {
        var first = await Register().HandleAsync(Command(), Ct);
        _time.Advance(TimeSpan.FromMinutes(5));
        var second = await Register().HandleAsync(Command(), Ct);

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : null);
        first.Value.BlockStart.ShouldBe(Three);
        first.Value.ConfigHash.Length.ShouldBe(64);
        second.Value.ConfigHash.ShouldBe(first.Value.ConfigHash);
        second.Value.Id.ShouldNotBe(first.Value.Id);
        var config = ReplayJson.Read<HoldOutRegistrationConfig>(_regs.Registrations[0].ConfigJson)!;
        (config.Parameters.CostSheetId, config.EngineVersion, config.Parameters.Mode, config.PassRule.MaxDrawdownPct).ShouldBe(("dhan-2026-04", "sim-5", ReplayMode.Frozen, 15d));
        config.Arms.ShouldBe(["selection", "baseline"]);
        config.Versions["S1"][0].Hash.ShouldBe(VersionHash);
        config.CostSheetRatesHash.ShouldBe(Sheet("dhan-2026-04").RatesSha256);
        _regs.Registrations[0].ConfigHash.ShouldBe(ReplayJson.Sha256(_regs.Registrations[0].ConfigJson));
    }

    [Fact]
    public async Task A_changed_sheet_rate_changes_the_hash()
    {
        var before = await Register().HandleAsync(Command(), Ct);
        _sheets.With(Sheet("dhan-2026-04", brokerage: 0.05m));

        var after = await Register().HandleAsync(Command(), Ct);

        after.Value.ConfigHash.ShouldNotBe(before.Value.ConfigHash);
    }

    [Theory]
    [InlineData(null, "gross_choice")]
    [InlineData("gross", "gross_choice")]
    [InlineData("nope", "sheet_missing")]
    [InlineData("unchecked-sheet", "sheet_unchecked")]
    public async Task A_missing_gross_or_unchecked_sheet_is_refused_with_its_reason_and_nothing_is_stored(string? sheet, string reason)
    {
        _sheets.With(Sheet("unchecked-sheet", checkedNote: false));

        var result = await Register().HandleAsync(Command(sheet), Ct);

        (result.Error.Code, result.Error.Kind, result.Error.Message.StartsWith(reason + ":", StringComparison.Ordinal)).ShouldBe((HoldOutRegistrationErrors.InvalidCode, ErrorKind.Validation, true));
        _regs.Registrations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_block_that_has_begun_leaves_no_future_block_and_a_scope_over_the_memory_cap_is_refused()
    {
        _members = 80;
        var capped = await Register().HandleAsync(Command(), Ct);
        _members = 2;
        _time.SetUtcNow(new DateTimeOffset(2027, 5, 5, 6, 0, 0, TimeSpan.Zero)); // block 3 begins on 2027-05-05; block 4 is not stored and not derived before block 3 is
        var begun = await Register().HandleAsync(Command(), Ct);

        capped.Error.Message.ShouldStartWith("scope_over_memory_cap:");
        begun.Error.Message.ShouldStartWith("no_future_block:");
        _regs.Registrations.ShouldBeEmpty();
    }

    // ---- the claim (AC 3, 4, 5)

    [Fact]
    public async Task The_first_run_claims_the_system_look_beside_strategy_and_model_looks_and_a_second_run_is_holdout_used()
    {
        var id = await SealedBlockThreeWithRegistrationAsync();
        _blocks.Unlocks.Add(new StoredUnlock("ORB", 1, "strategy", "owner", "owner:x", DateTimeOffset.UnixEpoch, Hash, false, Three, Guid.NewGuid(), View("ORB", "strategy")));
        _blocks.Unlocks.Add(new StoredUnlock("model-family:f1@2027-05-05", 1, "model", "owner", "owner:x", DateTimeOffset.UnixEpoch, Hash, false, Three, Guid.NewGuid(), View("m", "model")));

        var first = await Run().HandleAsync(new RunHoldOutRegistrationCommand(id, "owner", "owner:x"), Ct);
        var second = await Run().HandleAsync(new RunHoldOutRegistrationCommand(id, "owner", "owner:x"), Ct);

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : null);
        first.Value.RunIds.Count.ShouldBe(2);
        var look = _blocks.Unlocks.Single(u => u.Kind == "system");
        (look.ReusedWindow, look.Mode, look.Actor, look.BlockStart, look.StrategyId).ShouldBe((false, "owner", "owner:x", Three, HoldOutRegistrationIds.LookKey(id)));
        _blocks.Unlocks.Count(u => !u.ReusedWindow).ShouldBe(3);
        (second.Error.Code, second.Error.Kind).ShouldBe((HoldOutRegistrationErrors.UsedCode, ErrorKind.Conflict));
        _blocks.Unlocks.Count(u => u.Kind == "system").ShouldBe(1);
    }

    [Fact]
    public async Task A_replaced_sheet_is_drift_before_the_claim_and_a_missing_version_is_too_and_nothing_is_burned()
    {
        var id = await SealedBlockThreeWithRegistrationAsync();
        _sheets.With(Sheet("dhan-2026-04", brokerage: 0.05m));
        var sheetDrift = await Run().HandleAsync(new RunHoldOutRegistrationCommand(id, "owner", "owner:x"), Ct);
        _sheets.With(Sheet("dhan-2026-04"));
        _strategies.ListVersionsAsync("S1", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredVersion>>([]));
        var versionDrift = await Run().HandleAsync(new RunHoldOutRegistrationCommand(id, "owner", "owner:x"), Ct);

        (sheetDrift.Error.Code, sheetDrift.Error.Message).ShouldBe((HoldOutRegistrationErrors.DriftCode, sheetDrift.Error.Message));
        sheetDrift.Error.Message.ShouldContain("new content hash");
        versionDrift.Error.Code.ShouldBe(HoldOutRegistrationErrors.DriftCode);
        _blocks.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_changed_live_setting_or_engine_is_not_drift()
    {
        var id = await SealedBlockThreeWithRegistrationAsync();
        _engine.EngineVersion.Returns("sim-6");
        _facts.SettingsAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Snapshot() with { IncompleteDataShare = 0.5 }));

        var run = await Run().HandleAsync(new RunHoldOutRegistrationCommand(id, "owner", "owner:x"), Ct);

        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : null);
    }

    [Fact]
    public async Task A_block_that_is_still_filling_answers_the_filling_error_and_nothing_is_claimed()
    {
        var id = await SealedBlockThreeWithRegistrationAsync(sealedNow: false);

        var run = await Run().HandleAsync(new RunHoldOutRegistrationCommand(id, "owner", "owner:x"), Ct);

        run.Error.Code.ShouldBe(LabErrors.HoldOutFillingCode);
        _blocks.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_newest_registration_of_a_block_runs_and_the_older_one_is_superseded()
    {
        _blocks.Releases.Add(new StoredRelease(Two, DateTimeOffset.UnixEpoch, "owner", "owner:x", Hash, true, Three));
        _blocks.Blocks.Add(new HoldOutBlock(Three, new DateOnly(2024, 10, 1), ThreeEnd, 6, Hash, DateTimeOffset.UnixEpoch, Two));
        var older = await Register().HandleAsync(Command(), Ct);
        _time.Advance(TimeSpan.FromDays(1));
        var newer = await Register().HandleAsync(Command(), Ct);
        _time.SetUtcNow(new DateTimeOffset(2027, 11, 5, 6, 0, 0, TimeSpan.Zero));

        var oldRun = await Run().HandleAsync(new RunHoldOutRegistrationCommand(older.Value.Id, "owner", "owner:x"), Ct);
        var newRun = await Run().HandleAsync(new RunHoldOutRegistrationCommand(newer.Value.Id, "owner", "owner:x"), Ct);

        oldRun.Error.Code.ShouldBe(HoldOutRegistrationErrors.SupersededCode);
        newRun.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_registration_stored_on_or_after_the_block_start_cannot_take_a_fresh_look_and_stores_nothing()
    {
        var id = await SealedBlockThreeWithRegistrationAsync();
        var late = _regs.Registrations[0] with { CreatedAt = new DateTimeOffset(2027, 5, 5, 6, 0, 0, TimeSpan.Zero) };
        _regs.Registrations[0] = late;

        var run = await Run().HandleAsync(new RunHoldOutRegistrationCommand(id, "owner", "owner:x"), Ct);

        run.Error.Code.ShouldBe("replay.holdout_not_predated");
        _blocks.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public void A_claim_can_be_made_only_from_a_fresh_system_look_of_the_registration()
    {
        var id = Guid.NewGuid();
        StoredUnlock Look(string kind, bool reused, string key) => new(key, 1, kind, "owner", "owner:x", DateTimeOffset.UnixEpoch, Hash, reused, Three, Guid.NewGuid(), View(key, kind));
        var key = HoldOutRegistrationIds.LookKey(id);

        HoldOutClaim.From(Look("system", false, key), id).IsSuccess.ShouldBeTrue();
        HoldOutClaim.From(Look("system", true, key), id).IsFailure.ShouldBeTrue();
        HoldOutClaim.From(Look("strategy", false, key), id).IsFailure.ShouldBeTrue();
        HoldOutClaim.From(Look("system", false, HoldOutRegistrationIds.LookKey(Guid.NewGuid())), id).IsFailure.ShouldBeTrue();
    }

    // ---- the slot (AC 10)

    [Fact]
    public async Task The_system_slot_keeps_the_block_until_the_newest_registration_has_its_result()
    {
        var slot = new SystemLookSlot(_regs);
        (await slot.ReadAsync(Three, Ct)).ShouldBe(LookAvailability.NoSubject);

        var id = await SealedBlockThreeWithRegistrationAsync();
        (await slot.ReadAsync(Three, Ct)).ShouldBe(LookAvailability.Available);

        await _regs.InsertResultAsync(new StoredHoldOutResult(id, Guid.NewGuid(), [], "inconclusive", "{}", "sim-5", "owner", "owner:x", _time.GetUtcNow()), Ct);
        (await slot.ReadAsync(Three, Ct)).ShouldBe(LookAvailability.Used);
        slot.Kind.ShouldBe("system");
    }

    // ---- the auto step (AC 8, 9)

    private (SystemReplaySealStep Step, ICommandHandler<RegisterHoldOutCommand, RegisteredHoldOut> Register, ICommandHandler<RunHoldOutRegistrationCommand, StartedHoldOutRun> Run) Step()
    {
        var register = Substitute.For<ICommandHandler<RegisterHoldOutCommand, RegisteredHoldOut>>();
        register.HandleAsync(Arg.Any<RegisterHoldOutCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new RegisteredHoldOut(Guid.NewGuid(), Hash, Three))));
        var run = Substitute.For<ICommandHandler<RunHoldOutRegistrationCommand, StartedHoldOutRun>>();
        run.HandleAsync(Arg.Any<RunHoldOutRegistrationCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new StartedHoldOutRun([]))));
        return (new SystemReplaySealStep(Epochs(), _regs, _regs, register, run, new HoldOutOptions(), _time), register, run);
    }

    [Fact]
    public async Task On_the_evening_block_two_seals_the_auto_step_registers_for_block_three_once_as_auto()
    {
        _time.SetUtcNow(new DateTimeOffset(2027, 5, 4, 14, 0, 0, TimeSpan.Zero)); // block 2 (ends 2027-05-04) has closed; block 3 begins tomorrow
        var (step, register, _) = Step();

        var said = await step.RunAsync(Ct);

        await register.Received(1).HandleAsync(Arg.Is<RegisterHoldOutCommand>(c => c.Mode == "auto" && c.Actor == "system:holdout-replay" && c.CostSheetId == null), Arg.Any<CancellationToken>());
        said.ShouldContain("registered for 2027-05-05");
        step.Order.ShouldBe(40);
    }

    [Fact]
    public async Task The_auto_step_registers_nothing_with_an_owner_registration_stored_the_switch_off_or_the_switch_unavailable_and_nothing_before_the_block_seals()
    {
        var (step, register, run) = Step();
        (await step.RunAsync(Ct)).ShouldBe("no sealed block"); // 2027-04-20: block 2 is still filling
        _time.SetUtcNow(new DateTimeOffset(2027, 5, 4, 14, 0, 0, TimeSpan.Zero));
        _regs.Registrations.Add(new StoredHoldOutRegistration(Guid.NewGuid(), Three, "{}", Hash, "owner", "owner:x", _time.GetUtcNow()));
        (await step.RunAsync(Ct)).ShouldContain("registration exists");

        _regs.Registrations.Clear();
        _regs.Switches = new HoldOutAutoSwitches(false, false);
        (await step.RunAsync(Ct)).ShouldContain("auto register off");
        _regs.Switches = new HoldOutAutoSwitches(null, null);
        (await step.RunAsync(Ct)).ShouldContain("register unavailable");

        await register.DidNotReceive().HandleAsync(Arg.Any<RegisterHoldOutCommand>(), Arg.Any<CancellationToken>());
        await run.DidNotReceive().HandleAsync(Arg.Any<RunHoldOutRegistrationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task On_the_evening_block_three_seals_the_auto_step_claims_the_registration_once_as_auto_and_not_with_the_switch_off()
    {
        var id = await SealedBlockThreeWithRegistrationAsync();
        var (step, _, run) = Step();

        var said = await step.RunAsync(Ct);
        _regs.Switches = new HoldOutAutoSwitches(true, false);
        var off = await step.RunAsync(Ct);

        await run.Received(1).HandleAsync(Arg.Is<RunHoldOutRegistrationCommand>(c => c.RegistrationId == id && c.Mode == "auto"), Arg.Any<CancellationToken>());
        said.ShouldContain("look claimed");
        off.ShouldContain("auto run off");
    }

    private static HoldOutResultView View(string id, string kind) => new(id, 1, kind, "owner", "used", DateTimeOffset.UnixEpoch, Three, Three, false, false, true, "pass", "ok", null, null, null, 0, null, Hash, "owner:x", []);
}

/// <summary>An in-memory registration store and switch table, newest first as the table reads.</summary>
internal sealed class MemoryRegistrationStore : IHoldOutRegistrationStore, IHoldOutAutoSettings
{
    public List<StoredHoldOutRegistration> Registrations { get; } = [];

    public Dictionary<Guid, StoredHoldOutResult> Results { get; } = [];

    public HoldOutAutoSwitches Switches { get; set; } = new(true, true);

    public ValueTask InsertAsync(StoredHoldOutRegistration registration, CancellationToken cancellationToken)
    {
        Registrations.Add(registration);
        return ValueTask.CompletedTask;
    }

    public ValueTask<StoredHoldOutRegistration?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Registrations.FirstOrDefault(r => r.Id == id));

    public ValueTask<IReadOnlyList<StoredHoldOutRegistration>> ListAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredHoldOutRegistration>>([.. Registrations.OrderByDescending(r => r.CreatedAt).Take(limit)]);

    public ValueTask<IReadOnlyList<StoredHoldOutRegistration>> ForBlockAsync(DateOnly blockStart, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredHoldOutRegistration>>([.. Registrations.Where(r => r.BlockStart == blockStart).OrderByDescending(r => r.CreatedAt)]);

    public ValueTask<bool> InsertResultAsync(StoredHoldOutResult result, CancellationToken cancellationToken) => ValueTask.FromResult(Results.TryAdd(result.RegistrationId, result));

    public ValueTask<StoredHoldOutResult?> GetResultAsync(Guid registrationId, CancellationToken cancellationToken) => ValueTask.FromResult(Results.GetValueOrDefault(registrationId));

    public ValueTask<HoldOutAutoSwitches> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Switches);

    public ValueTask AppendAsync(string name, bool enabled, string actor, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Switches = name == HoldOutRegistrationErrors.AutoRegister ? Switches with { AutoRegister = enabled } : Switches with { AutoRun = enabled };
        return ValueTask.CompletedTask;
    }
}
