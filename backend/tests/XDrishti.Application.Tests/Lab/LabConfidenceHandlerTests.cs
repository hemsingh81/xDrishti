using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-06 AC2 and AC12: the registration handler, the report rows (state, count with its parts, hold-out state, no numbers before the unlock) and the ledger read.</summary>
public sealed class LabConfidenceHandlerTests
{
    private static readonly string Hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();
    private readonly IRegistrationStore _registrations = Substitute.For<IRegistrationStore>();
    private readonly IPlanFreezeStore _freezes = Substitute.For<IPlanFreezeStore>();
    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();
    private readonly ILabResultStore _results = Substitute.For<ILabResultStore>();
    private readonly MemoryHoldOutStore _holdOuts = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));

    public LabConfidenceHandlerTests() => _freezes.FirstAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((string?)null);

    private RegisterStrategyHandler Register() => new(_strategies, _registrations, _freezes, _time, NullLogger<RegisterStrategyHandler>.Instance);

    private static RegisterStrategyCommand Command(string? falsifier = "retire if OOS under 0.05 R", string actor = "owner:x", string id = "FAM", int version = 2) =>
        new(id, version, "a forced seller", "period: negative", "tier A, first hour", "40 bps gross against 12 bps cost", falsifier, actor);

    private void Stored(string id = "FAM", int version = 2) =>
        _strategies.GetVersionAsync(id, version, Arg.Any<CancellationToken>()).Returns(new StoredVersion(id, version, 1, Hash, "t", "{}", null, null, DateTimeOffset.UnixEpoch));

    [Fact]
    public async Task A_valid_registration_is_stored_with_its_hash_the_owner_and_the_time_and_served_back()
    {
        Stored();
        StoredRegistration? written = null;
        _registrations.InsertAsync(Arg.Do<StoredRegistration>(r => written = r), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Register().HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        written.ShouldNotBeNull().TextHash.ShouldBe(written.Registration.TextHash());
        result.Value.TextHash.ShouldBe(written.TextHash);
        (result.Value.Actor, result.Value.CreatedAt, result.Value.Version).ShouldBe(("owner:x", _time.GetUtcNow(), 2));
    }

    [Fact]
    public async Task A_second_registration_of_the_same_version_is_refused_as_frozen_and_a_new_version_is_its_own()
    {
        Stored();
        Stored(version: 3);
        _registrations.InsertAsync(Arg.Is<StoredRegistration>(r => r.Version == 2), Arg.Any<CancellationToken>()).Returns(false);
        _registrations.InsertAsync(Arg.Is<StoredRegistration>(r => r.Version == 3), Arg.Any<CancellationToken>()).Returns(true);

        var again = await Register().HandleAsync(Command(), Ct);
        var next = await Register().HandleAsync(Command(version: 3), Ct);

        again.Error.Code.ShouldBe(LabErrors.RegistrationFrozenCode);
        again.Error.Kind.ShouldBe(ErrorKind.Conflict);
        next.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_registration_after_the_plan_was_frozen_by_a_full_run_is_refused_with_a_409_and_nothing_is_written()
    {
        Stored();
        _freezes.FirstAsync("FAM", 2, Arg.Any<CancellationToken>()).Returns(new string('f', 64));

        var late = await Register().HandleAsync(Command(), Ct);

        late.Error.Code.ShouldBe(LabErrors.RegistrationFrozenCode);
        late.Error.Kind.ShouldBe(ErrorKind.Conflict);
        late.Error.Message.ShouldContain("frozen plan");
        await _registrations.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
    }

    [Fact]
    public async Task An_empty_part_an_unknown_version_and_a_missing_owner_are_refused_and_nothing_is_written()
    {
        Stored();

        var empty = await Register().HandleAsync(Command(falsifier: " "), Ct);
        var unknown = await Register().HandleAsync(Command(id: "NOPE"), Ct);
        var nobody = await Register().HandleAsync(Command(actor: ""), Ct);

        empty.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        empty.Error.Fields!.Single().Field.ShouldBe("falsifier");
        unknown.Error.Kind.ShouldBe(ErrorKind.NotFound);
        nobody.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        await _registrations.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
    }

    [Fact]
    public async Task The_registration_read_gives_the_texts_or_not_found()
    {
        _registrations.GetAsync("FAM", 2, Arg.Any<CancellationToken>()).Returns(new StoredRegistration("FAM", 2, Registration.Create("a", "b", "c", "d", "e").Value, new string('c', 64), DateTimeOffset.UnixEpoch, "owner:x"));
        var handler = new GetRegistrationHandler(_registrations);

        (await handler.HandleAsync(new GetRegistrationQuery("FAM", 2), Ct)).Value.Falsifier.ShouldBe("e");
        (await handler.HandleAsync(new GetRegistrationQuery("FAM", 9), Ct)).Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    private static LabVerdictView Verdict() => new("PASS", 3, 1.5, new EvidenceView(400, 0.4, 1.6, 6, 5, 8, 0, "walk_forward"), [new GateView("oos_trades", 300, 375, 400, true, "ok")], [], [], [], ["x"],
        Trials: TrialsView.Of(new TrialCounts(3, 16, 9, 0, 0), false, true));

    private GetLabReportHandler ReportHandler(RegistrationOptions? registration = null, HoldOutOptions? holdOut = null, bool withStores = true)
    {
        var run = new LabRun(Guid.NewGuid(), new string('k', 64), "FAM", 2, Hash, new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null),
            new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "full", new string('c', 64), "sim-5", LabRunState.Succeeded, null, 0, 0, ["sk"], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        _runs.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(run);
        _results.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Verdict());
        return withStores
            ? new GetLabReportHandler(_runs, _results, LabSettings.Default, _registrations, _holdOuts, registration, holdOut)
            : new GetLabReportHandler(_runs, _results, LabSettings.Default);
    }

    [Fact]
    public async Task Without_the_stores_the_report_has_neither_registration_nor_hold_out_rows_as_it_was()
    {
        var report = (await ReportHandler(withStores: false).HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;

        (report.Registration, report.HoldOut).ShouldBe((null, null));
        report.Warnings.ShouldBeEmpty();
        report.Trials.ShouldNotBeNull().Total.ShouldBe(28);
    }

    [Fact]
    public async Task An_unregistered_version_reads_not_registered_with_a_warning_and_nothing_is_refused_while_enforcement_is_off()
    {
        var report = (await ReportHandler().HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;

        report.Registration.ShouldNotBeNull().State.ShouldBe("not_registered");
        report.Registration.Enforce.ShouldBeFalse();
        report.Warnings.ShouldContain(w => w.Code == "not_registered");
        report.Outcome.ShouldBe("PASS");
    }

    [Fact]
    public async Task A_registered_version_shows_its_texts_and_no_warning()
    {
        _registrations.GetAsync("FAM", 2, Arg.Any<CancellationToken>()).Returns(new StoredRegistration("FAM", 2, Registration.Create("a", "b", "c", "d", "e").Value, new string('c', 64), DateTimeOffset.UnixEpoch, "owner:x"));

        var report = (await ReportHandler(new RegistrationOptions(true)).HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;

        report.Registration.ShouldNotBeNull().State.ShouldBe("registered");
        (report.Registration.Enforce, report.Registration.Registration!.Counterparty).ShouldBe((true, "a"));
        report.Warnings.ShouldNotContain(w => w.Code == "not_registered");
    }

    [Fact]
    public async Task The_hold_out_row_walks_not_fixed_kept_used_and_never_shows_numbers_before_the_look()
    {
        var notFixed = (await ReportHandler().HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;
        _holdOuts.Blocks.Add(new HoldOutBlock(new DateOnly(2026, 5, 1), new DateOnly(2024, 10, 1), new DateOnly(2026, 10, 30), 6, new string('d', 64), DateTimeOffset.UnixEpoch));
        var kept = (await ReportHandler().HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;
        var result = new HoldOutResultView("FAM", 2, "strategy", "owner", "used", DateTimeOffset.UnixEpoch, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30), false, false, true, "pass", "r", 0.2, 0.12, 0.6, 45, null, new string('s', 64), "owner:x", []);
        _holdOuts.Unlocks.Add(new StoredUnlock("FAM", 2, "strategy", "owner", "owner:x", DateTimeOffset.UnixEpoch, new string('s', 64), false, new DateOnly(2026, 5, 1), Guid.NewGuid(), result));
        var used = (await ReportHandler().HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;
        _holdOuts.Unlocks[0] = _holdOuts.Unlocks[0] with { Result = result with { State = "reused", ReusedWindow = true, Passed = false } };
        var reused = (await ReportHandler().HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;

        notFixed.HoldOut.ShouldNotBeNull().State.ShouldBe("not_fixed");
        notFixed.Warnings.ShouldContain(w => w.Code == "holdout_not_kept");
        kept.HoldOut.ShouldNotBeNull().State.ShouldBe("kept");
        kept.HoldOut.Start.ShouldBe(new DateOnly(2026, 5, 1));
        kept.HoldOut.Result.ShouldBeNull("never the numbers before the unlock");
        kept.HoldOut.Text.ShouldContain("kept, not yet used");
        kept.HoldOut.RunReachesHoldOut.ShouldBeFalse();
        used.HoldOut.ShouldNotBeNull().State.ShouldBe("used");
        used.HoldOut.Result.ShouldNotBeNull().HoldOutExpectancyR.ShouldBe(0.12);
        reused.HoldOut.ShouldNotBeNull().State.ShouldBe("reused");
        reused.HoldOut.Result!.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_whose_window_reached_the_block_says_so_and_warns_while_the_hold_out_is_unused()
    {
        _holdOuts.Blocks.Add(new HoldOutBlock(new DateOnly(2026, 4, 1), new DateOnly(2024, 10, 1), new DateOnly(2026, 10, 30), 6, new string('d', 64), DateTimeOffset.UnixEpoch));

        var report = (await ReportHandler().HandleAsync(new GetLabReportQuery(Guid.NewGuid()), Ct)).Value;

        report.HoldOut.ShouldNotBeNull().RunReachesHoldOut.ShouldBeTrue();
        report.Warnings.ShouldContain(w => w.Code == "holdout_not_kept");
    }

    [Fact]
    public void The_trials_view_prints_the_parts_the_honest_factor_and_the_factor_used()
    {
        var off = TrialsView.Of(new TrialCounts(3, 16, 9, 0, 0), false, true);
        var on = TrialsView.Of(new TrialCounts(3, 16, 9, 0, 0), true, true);
        var noAdjust = TrialsView.Of(new TrialCounts(3, 16, 9, 0, 0), true, false);

        (off.Total, off.JudgedCount).ShouldBe((28, 19));
        off.FactorUsed.ShouldBe(2.0620, 1e-4);
        on.JudgedCount.ShouldBe(28);
        on.FactorUsed.ShouldBe(2.2018, 1e-4);
        off.Text.ShouldContain("28 looks = 3 strategy hashes + 16 sweep points + 9 extra exit candidates + 0 re-plans + 0 nulls");
        off.Text.ShouldContain("2.2018");
        (noAdjust.JudgedCount, noAdjust.FactorUsed).ShouldBe((1, 1.0));
    }
}
