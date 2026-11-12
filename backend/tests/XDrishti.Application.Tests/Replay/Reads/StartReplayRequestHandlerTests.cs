#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Reads;

/// <summary>SR-01 part C, AC 1 and 2: the request becomes the parameters of one run or of a frozen and a learning run, the scope comes from the as-of auto scope when none is given, and bad input or a busy replay is refused before anything is stored.</summary>
public sealed class StartReplayRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly From = new(2025, 1, 6);
    private static readonly DateOnly Last = new(2026, 6, 30);

    private sealed class Rig
    {
        public Rig(bool running = false)
        {
            Facts.LastReadableSessionAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Last));
            Reads.AnyRunningAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(running));
            Profiles.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<XDrishti.Domain.Accounts.RiskProfile>>([]));
            Start.HandleAsync(Arg.Do<StartReplayCommand>(c => Sent = c), Arg.Any<CancellationToken>())
                .Returns(c => ValueTask.FromResult(Result.Success(new StartedReplay([.. ((StartReplayCommand)c[0]).Runs.Select(_ => Guid.NewGuid())], null, 1))));
        }

        public IReplayFacts Facts { get; } = Substitute.For<IReplayFacts>();

        public IReplayReads Reads { get; } = Substitute.For<IReplayReads>();

        public IAutoScopeStore Scope { get; } = Substitute.For<IAutoScopeStore>();

        public IRiskProfileStore Profiles { get; } = Substitute.For<IRiskProfileStore>();

        public ICommandHandler<StartReplayCommand, StartedReplay> Start { get; } = Substitute.For<ICommandHandler<StartReplayCommand, StartedReplay>>();

        public StartReplayCommand? Sent { get; private set; }

        public ValueTask<Result<StartedReplay>> RunAsync(StartReplayRequest request) =>
            new StartReplayRequestHandler(Facts, Reads, Scope, Profiles, Start).HandleAsync(request, Ct);
    }

    private static StartReplayRequest Request(IReadOnlyList<string>? modes = null, string? versions = null, IReadOnlyList<Guid>? baskets = null, DateOnly? to = null) =>
        new(From, to, baskets, null, null, null, null, versions, modes, false, false);

    [Fact]
    public async Task The_defaults_are_the_last_readable_session_the_standard_profile_gross_start_rule_and_frozen()
    {
        var rig = new Rig();

        var result = await rig.RunAsync(Request(baskets: [Guid.NewGuid()]));

        result.IsSuccess.ShouldBeTrue();
        var p = rig.Sent!.Runs.Single();
        (p.To, p.Capital, p.Profile, p.CostSheetId, p.VersionRule, p.Mode).ShouldBe((Last, 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen));
    }

    [Fact]
    public async Task Both_modes_start_a_frozen_and_a_learning_run_that_share_everything_else()
    {
        var rig = new Rig();

        await rig.RunAsync(Request(modes: ["frozen", "learning"], versions: "asOf", baskets: [Guid.NewGuid()]));

        rig.Sent!.Runs.Select(r => r.Mode).ShouldBe([ReplayMode.Frozen, ReplayMode.Learning]);
        rig.Sent.Runs.Select(r => r with { Mode = ReplayMode.Frozen }).Distinct().Count().ShouldBe(1);
        rig.Sent.Runs[0].VersionRule.ShouldBe(ReplayVersionRule.AsOf);
    }

    [Fact]
    public async Task With_no_scope_given_the_stored_auto_scope_is_used_with_its_baskets_kept_as_baskets()
    {
        var rig = new Rig();
        var (basket, instrument) = (Guid.NewGuid(), Guid.NewGuid());
        rig.Scope.LatestAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredAutoScope?>(new StoredAutoScope(1, [basket], [instrument], "h", "owner", DateTimeOffset.UnixEpoch)));

        await rig.RunAsync(Request());

        var scope = rig.Sent!.Runs.Single().Scope;
        scope.BasketIds.ShouldBe([basket]);
        scope.InstrumentIds.ShouldBe([instrument]);
    }

    [Fact]
    public async Task More_baskets_or_instruments_than_the_caps_are_replay_invalid_before_anything_is_read()
    {
        var rig = new Rig();

        var baskets = await rig.RunAsync(Request(baskets: [.. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid())]));
        var instruments = await rig.RunAsync(Request() with { InstrumentIds = [.. Enumerable.Range(0, 2001).Select(_ => Guid.NewGuid())] });

        (baskets.Error.Code, instruments.Error.Code).ShouldBe(("replay.invalid", "replay.invalid"));
        rig.Sent.ShouldBeNull();
        await rig.Facts.DidNotReceive().LastReadableSessionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("hourly", null)]
    [InlineData("frozen", "yesterday")]
    public async Task An_unknown_mode_or_version_rule_is_replay_invalid_and_stores_nothing(string mode, string? versions)
    {
        var rig = new Rig();

        var result = await rig.RunAsync(Request(modes: [mode], versions: versions));

        result.Error.Code.ShouldBe("replay.invalid");
        rig.Sent.ShouldBeNull();
    }

    [Fact]
    public async Task A_replay_that_is_running_is_busy_before_anything_is_prepared()
    {
        var rig = new Rig(running: true);

        var result = await rig.RunAsync(Request(baskets: [Guid.NewGuid()]));

        result.Error.Code.ShouldBe("replay.busy");
        rig.Sent.ShouldBeNull();
    }

    [Fact]
    public async Task No_hold_out_fixed_is_replay_holdout_when_the_end_date_is_left_to_the_default()
    {
        var rig = new Rig();
        rig.Facts.LastReadableSessionAsync(Arg.Any<CancellationToken>()).Returns<DateOnly>(_ => throw new ReplayHoldOutException());

        var result = await rig.RunAsync(Request(baskets: [Guid.NewGuid()]));

        result.Error.Code.ShouldBe("replay.holdout");
    }
}
