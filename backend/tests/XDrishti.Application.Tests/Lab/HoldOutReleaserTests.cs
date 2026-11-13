#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Lab;
using XDrishti.Application.Replay;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Lab;

/// <summary>A weekday-only calendar for the tests (no holiday is confirmed).</summary>
internal sealed class FixedCalendar : ITradingCalendar
{
    public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

    public void Invalidate()
    {
    }
}

/// <summary>HO-01 (ADR 0283): the states of the chain, the owner's release and its refusals, the auto release, and the cut that follows the block in force.</summary>
public sealed class HoldOutReleaserTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly DateOnly One = new(2026, 5, 1);
    private static readonly DateOnly OneEnd = new(2026, 10, 30);
    private static readonly DateOnly Two = new(2026, 10, 31);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly MemoryHoldOutStore _store = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));

    public HoldOutReleaserTests() =>
        _store.Blocks.Add(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UnixEpoch));

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();
    private readonly IAccountUnitOfWork _unitOfWork = Substitute.For<IAccountUnitOfWork>();

    // the strategy slot is registered by HO-01b; the tests stand in for it unless they say otherwise
    private HoldOutReleaser Releaser(HoldOutOptions? options = null, params IHoldOutLookSlot[] slots) =>
        new(Epochs(), _store, slots.Length == 0 ? [Slot("strategy", LookAvailability.Used)] : slots, options ?? new HoldOutOptions(), _time, _events, _unitOfWork, NullLogger<HoldOutReleaser>.Instance);

    private HoldOutEpochs Epochs() => new(_store, new FixedCalendar(), _time);

    private void Look(DateOnly block, bool reused = false) =>
        _store.Unlocks.Add(new StoredUnlock("ORB", _store.Unlocks.Count + 1, "strategy", "owner", "owner:x", _time.GetUtcNow(), Hash, reused, block, Guid.NewGuid(), new HoldOutResultView("ORB", 1, "strategy", "owner", reused ? "reused" : "used", _time.GetUtcNow(), block, block, reused, false, true, "pass", "ok", null, null, null, 0, null, Hash, "owner:x", [])));

    private static FakeSlot Slot(string kind, LookAvailability state) => new(kind, state);

    private sealed class FakeSlot(string kind, LookAvailability state) : IHoldOutLookSlot
    {
        public string Kind { get; } = kind;

        public ValueTask<LookAvailability> ReadAsync(DateOnly blockStart, CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    [Fact]
    public async Task A_sealed_block_is_sealed_then_looked_after_a_fresh_look_and_a_re_used_look_does_not_count()
    {
        Look(One, reused: true);
        (await Epochs().StatusAsync(Ct)).Single().State.ShouldBe(HoldOutBlockState.Sealed);

        Look(One);

        (await Epochs().StatusAsync(Ct)).Single().State.ShouldBe(HoldOutBlockState.Looked);
    }

    [Fact]
    public async Task A_block_whose_last_session_has_not_closed_is_filling()
    {
        var open = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero)); // 14:30 India time on the last day: the session is still open

        (await new HoldOutEpochs(_store, new FixedCalendar(), open).StatusAsync(Ct)).Single().State.ShouldBe(HoldOutBlockState.Filling);
    }

    [Fact]
    public async Task The_owner_releases_a_looked_block_and_one_transaction_stores_the_release_and_block_two()
    {
        Look(One);

        var result = await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.State.ShouldBe("released");
        result.Value.Mode.ShouldBe("owner");
        var release = _store.Releases.Single();
        (release.Mode, release.Actor, release.Looked, release.NextBlockStart, release.SettingHash).ShouldBe(("owner", "owner:x", true, Two, new HoldOutOptions().SnapshotHash()));
        var next = _store.Blocks.Single(b => b.Start == Two);
        (next.HistoryLast, next.LengthMonths, next.PrevStart).ShouldBe((new DateOnly(2027, 4, 30), 6, One));
        _store.Blocks.Single(b => b.Start == One).HistoryLast.ShouldBe(OneEnd, "block 1 is never changed");
    }

    [Fact]
    public async Task A_block_after_a_change_of_the_length_uses_the_new_length_and_block_one_is_left_alone()
    {
        Look(One);

        await Releaser(new HoldOutOptions(BlockMonths: 3)).ReleaseByOwnerAsync(One, false, "owner:x", Ct);

        _store.Blocks.Single(b => b.Start == Two).HistoryLast.ShouldBe(new DateOnly(2027, 1, 30));
        _store.Blocks.Single(b => b.Start == One).LengthMonths.ShouldBe(6);
    }

    [Fact]
    public async Task An_unlooked_block_is_released_only_with_the_forfeit_and_the_row_says_it_was_not_looked()
    {
        var refused = await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct);

        refused.Error.Code.ShouldBe(LabErrors.HoldOutUnlookedCode);
        _store.Releases.ShouldBeEmpty();
        var done = await Releaser().ReleaseByOwnerAsync(One, true, "owner:x", Ct);

        done.IsSuccess.ShouldBeTrue();
        _store.Releases.Single().Looked.ShouldBeFalse();
    }

    [Fact]
    public async Task A_second_release_a_filling_block_and_a_block_out_of_order_are_refused_with_their_codes()
    {
        Look(One);
        await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct);

        (await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct)).Error.Code.ShouldBe(LabErrors.HoldOutReleasedCode);
        (await Releaser().ReleaseByOwnerAsync(Two, true, "owner:x", Ct)).Error.Code.ShouldBe(LabErrors.HoldOutFillingCode);
        (await Releaser().ReleaseByOwnerAsync(new DateOnly(2027, 5, 1), true, "owner:x", Ct)).Error.Code.ShouldBe(LabErrors.HoldOutOrderCode);
        (await Releaser().ReleaseByOwnerAsync(new DateOnly(2026, 6, 1), true, "owner:x", Ct)).Error.Code.ShouldBe(LabErrors.NotFoundCode);
        _store.Releases.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Nothing_is_released_without_an_owner_or_a_block_or_when_the_store_cannot_be_read()
    {
        (await Releaser().ReleaseByOwnerAsync(One, true, " ", Ct)).Error.Code.ShouldBe("lab.no_owner");
        _store.Failing = true;

        await Should.ThrowAsync<InvalidOperationException>(async () => await Releaser().ReleaseByOwnerAsync(One, true, "owner:x", Ct));

        _store.Releases.ShouldBeEmpty();
        _store.Failing = false;
        _store.Blocks.Clear();
        (await Releaser().ReleaseByOwnerAsync(One, true, "owner:x", Ct)).Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
    }

    [Fact]
    public async Task A_lost_race_on_the_next_block_releases_nothing()
    {
        Look(One);
        _store.Blocks.Add(new HoldOutBlock(Two, new DateOnly(2024, 10, 1), new DateOnly(2027, 4, 30), 6, Hash, DateTimeOffset.UnixEpoch));

        var result = await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct);

        result.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        _store.Releases.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_auto_release_off_releases_nothing_and_on_it_releases_a_looked_block_recorded_as_auto_with_the_snapshot()
    {
        Look(One);

        (await Releaser(new HoldOutOptions(AutoRelease: false)).TryAutoAsync(Ct)).ShouldBe(new AutoReleaseResult(false, "off"));
        _store.Releases.ShouldBeEmpty();
        var done = await Releaser(new HoldOutOptions(), Slot("strategy", LookAvailability.Used), Slot("model", LookAvailability.NoSubject)).TryAutoAsync(Ct);

        done.Released.ShouldBeTrue();
        var release = _store.Releases.Single();
        (release.Mode, release.Actor, release.Looked, release.SettingHash).ShouldBe(("auto", "system:auto-release", true, new HoldOutOptions().SnapshotHash()));
    }

    [Fact]
    public async Task The_auto_release_fails_closed_when_no_strategy_slot_is_registered()
    {
        Look(One);
        var bare = new HoldOutReleaser(Epochs(), _store, [], new HoldOutOptions(), _time, _events, _unitOfWork, NullLogger<HoldOutReleaser>.Instance);

        (await bare.TryAutoAsync(Ct)).ShouldBe(new AutoReleaseResult(false, "no_strategy_slot"));

        _store.Releases.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_release_writes_one_audit_row_naming_the_mode_and_the_actor_and_a_failed_audit_does_not_undo_it()
    {
        Look(One);
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("audit down"));

        await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct);

        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.LabHoldOutReleased && e.Actor == "owner:x" && e.Detail!.Contains("mode=owner", StringComparison.Ordinal)));
        _store.Releases.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_auto_release_never_forfeits_an_unlooked_block()
    {
        var result = await Releaser().TryAutoAsync(Ct);

        result.ShouldBe(new AutoReleaseResult(false, "unlooked"));
        _store.Releases.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(LookAvailability.Available, "look_available")]
    [InlineData(LookAvailability.Unavailable, "unavailable")]
    public async Task The_auto_release_keeps_a_block_while_a_look_is_available_or_a_read_is_unavailable(LookAvailability kind, string reason)
    {
        Look(One);

        var result = await Releaser(new HoldOutOptions(), Slot("strategy", LookAvailability.Used), Slot("model", kind)).TryAutoAsync(Ct);

        result.ShouldBe(new AutoReleaseResult(false, reason));
        _store.Releases.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_auto_release_fails_closed_when_the_store_cannot_be_read()
    {
        Look(One);
        _store.Failing = true;

        await Should.ThrowAsync<InvalidOperationException>(async () => await Releaser().TryAutoAsync(Ct));

        _store.Releases.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_clamp_is_unchanged_with_nothing_released_and_moves_forward_to_block_two_after_the_release()
    {
        var clamp = () => new ClampedBars(_store, new HoldOutOptions(), Substitute.For<IBarStore>(), Substitute.For<IBackfillSeams>(), Substitute.For<IBarQueries>());
        var before = await clamp().LastReadableAsync(Ct);
        Look(One);

        await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct);
        var after = await clamp().LastReadableAsync(Ct);

        before.ShouldBe(One.AddDays(-1 - 30), "the byte-identical cut of today: start less one day less the embargo");
        after.ShouldBe(Two.AddDays(-1 - 30));
        (await clamp().LastReadableAsync(Ct)).ShouldBeLessThan(Two);
    }

    [Fact]
    public async Task The_released_blocks_for_the_seen_holdout_label_are_none_before_a_release_and_block_one_after()
    {
        Look(One);
        (await Epochs().ReleasedAsync(Ct)).ShouldBeEmpty();

        await Releaser().ReleaseByOwnerAsync(One, false, "owner:x", Ct);

        (await Epochs().ReleasedAsync(Ct)).ShouldBe([(One, OneEnd)]);
    }
}
