using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.History;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>CF-04e: the order and the audit of the two owner acts (the password and the switch are the endpoint's).</summary>
public sealed partial class HistoryActsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("^[a-z_=0-9 .\\-]+$")]
    private static partial Regex Allowed();

    private sealed class Rig
    {
        public IServiceRunStore Runs { get; } = Substitute.For<IServiceRunStore>();

        public IAccountEventLog Events { get; } = Substitute.For<IAccountEventLog>();

        public ICommandHandler<RunBackfillCommand, BackfillSummary> DryRun { get; } = Substitute.For<ICommandHandler<RunBackfillCommand, BackfillSummary>>();

        public ICommandHandler<RunServiceNowCommand, ServiceRunView> RunNow { get; } = Substitute.For<ICommandHandler<RunServiceNowCommand, ServiceRunView>>();

        public ICommandHandler<ProbeSeamCommand, ProbeSeamSummary> Probe { get; } = Substitute.For<ICommandHandler<ProbeSeamCommand, ProbeSeamSummary>>();

        public IIntradayBarSource Source { get; } = Substitute.For<IIntradayBarSource>();

        public IDataAccountStatus Account { get; } = Substitute.For<IDataAccountStatus>();

        public FakeTimeProvider Clock { get; set; } = At(21);

        public List<AccountEvent> Recorded { get; } = [];

        public Rig()
        {
            Runs.GetRunningServicesAsync(Ct).ReturnsForAnyArgs(new HashSet<string>());
            Events.When(e => e.Record(Arg.Any<AccountEvent>())).Do(c => Recorded.Add(c.Arg<AccountEvent>()));
            Source.CheckAsync(Ct).ReturnsForAnyArgs(SourceAvailability.Ready);
            Account.GetAsync(Ct).ReturnsForAnyArgs(new DataAccountState(Guid.NewGuid(), "d", DataAccountStatuses.Connected, null));
            DryRun.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Success(Summary(23)));
            RunNow.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Success(new ServiceRunView(Guid.NewGuid(), "history-backfill", "manual", 1, DateTimeOffset.UnixEpoch, null, "running", null, null)));
            Probe.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Success(new ProbeSeamSummary(3, 2, 1, 0, 0, 3, ["a", "b", "c"])));
        }

        public HistoryActGuard Guard() => new(Runs, new FakeBackfillSettings(), Events, Substitute.For<IPlatformUnitOfWork>(), Clock);

        public StartHistoryBackfillHandler Start(HistoryProbeGate? gate = null) => new(Guard(), gate ?? new HistoryProbeGate(), DryRun, RunNow, Source, Account);

        public ProbeHistorySeamHandler ProbeHandler(HistoryProbeGate? gate = null) => new(Guard(), gate ?? new HistoryProbeGate(), Probe);
    }

    private static BackfillSummary Summary(int pending) => new(true, 3, 0, 0, pending, 0, 0, 0, 0, 0, 0, 0, 58_181_250, pending, 1.5, null);

    private static FakeTimeProvider At(int hourIst, int minute = 0) => new(new DateTimeOffset(2026, 11, 12, hourIst, minute, 0, IndiaTime.Offset));

    [Theory]
    [InlineData(14, 0, false)]
    [InlineData(19, 59, false)]
    [InlineData(20, 0, true)]
    [InlineData(21, 0, true)]
    [InlineData(5, 59, true)]
    [InlineData(6, 0, false)]
    public async Task The_window_edges_decide(int hour, int minute, bool passes)
    {
        var rig = new Rig { Clock = At(hour, minute) };

        var result = await rig.Start().HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);

        result.IsSuccess.ShouldBe(passes);
        if (!passes)
        {
            result.Error.Code.ShouldBe("history.outside_window");
            result.Error.Message.ShouldContain("20:00");
            await rig.DryRun.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        }
    }

    [Fact]
    public async Task A_running_service_refuses_both_acts_before_anything_else()
    {
        var rig = new Rig();
        rig.Runs.GetRunningServicesAsync(Ct).ReturnsForAnyArgs(new HashSet<string> { "history-backfill" });

        var start = await rig.Start().HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);
        var probe = await rig.ProbeHandler().HandleAsync(new ProbeHistorySeamCommand("owner:o@x"), Ct);

        start.Error.Code.ShouldBe("history.run_active");
        probe.Error.Code.ShouldBe("history.run_active");
        await rig.DryRun.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await rig.Probe.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_second_probe_while_one_runs_is_refused()
    {
        var rig = new Rig();
        var gate = new HistoryProbeGate();
        gate.TryEnter().ShouldBeTrue();

        var result = await rig.ProbeHandler(gate).HandleAsync(new ProbeHistorySeamCommand("owner:o@x"), Ct);

        result.Error.Code.ShouldBe("history.run_active");
        await rig.Probe.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task The_gate_is_released_after_a_probe()
    {
        var rig = new Rig();
        var gate = new HistoryProbeGate();

        (await rig.ProbeHandler(gate).HandleAsync(new ProbeHistorySeamCommand("owner:o@x"), Ct)).IsSuccess.ShouldBeTrue();

        gate.TryEnter().ShouldBeTrue();
    }

    [Fact]
    public async Task A_dry_run_refusal_is_passed_on_and_nothing_starts()
    {
        var rig = new Rig();
        rig.DryRun.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Failure<BackfillSummary>(BackfillErrors.DiskLow(6_970_000_000, 6_980_000_000, 58_181_250)));

        var result = await rig.Start().HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);

        result.Error.Code.ShouldBe("history.disk_low");
        await rig.RunNow.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Recorded.Single().Detail.ShouldBe("refused=history.disk_low");
    }

    [Fact]
    public async Task Pending_work_starts_the_service_run_and_zero_pending_starts_nothing()
    {
        var rig = new Rig();

        var started = await rig.Start().HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);
        rig.DryRun.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Success(Summary(0)));
        rig.RunNow.ClearReceivedCalls();
        var none = await rig.Start().HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);

        started.Value.Started.ShouldBeTrue();
        started.Value.ChunksPending.ShouldBe(23);
        started.Value.RunId.ShouldNotBeNull();
        none.Value.Started.ShouldBeFalse();
        await rig.RunNow.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Audit_details_are_counts_and_codes_only()
    {
        var rig = new Rig();

        await rig.Start().HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);
        await rig.ProbeHandler().HandleAsync(new ProbeHistorySeamCommand("owner:o@x"), Ct);

        rig.Recorded.Count.ShouldBe(2);
        rig.Recorded.Select(e => e.Event).ShouldBe([AccountEventType.HistoryBackfillStarted, AccountEventType.HistorySeamProbed]);
        rig.Recorded[1].Detail.ShouldBe("probed=3 passed=2 unresolved=1 no_overlap=0 failed=0 requests=3");
        foreach (var e in rig.Recorded)
        {
            Allowed().IsMatch(e.Detail!).ShouldBeTrue(e.Detail);
            e.Detail!.Length.ShouldBeLessThanOrEqualTo(AccountEvent.MaxDetailLength);
        }
    }

    [Fact]
    public async Task A_probe_in_the_request_refuses_a_start_and_the_gate_is_released_after_a_start()
    {
        var rig = new Rig();
        var gate = new HistoryProbeGate();
        gate.TryEnter().ShouldBeTrue();

        var refused = await rig.Start(gate).HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);
        gate.Leave();
        var started = await rig.Start(gate).HandleAsync(new StartHistoryBackfillCommand("owner:o@x"), Ct);

        refused.Error.Code.ShouldBe("history.run_active");
        await rig.DryRun.ReceivedWithAnyArgs(1).HandleAsync(default!, Ct); // only the second call got as far as the dry run
        started.IsSuccess.ShouldBeTrue();
        gate.TryEnter().ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("owner:")]
    [InlineData("system:scheduler")]
    public async Task An_empty_or_non_owner_actor_is_refused_by_both_acts_before_anything_happens(string actor)
    {
        var rig = new Rig();

        var start = await rig.Start().HandleAsync(new StartHistoryBackfillCommand(actor), Ct);
        var probe = await rig.ProbeHandler().HandleAsync(new ProbeHistorySeamCommand(actor), Ct);

        start.Error.Code.ShouldBe("history.owner_only");
        probe.Error.Code.ShouldBe("history.owner_only");
        await rig.DryRun.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await rig.Probe.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Recorded.ShouldBeEmpty();
    }
}
