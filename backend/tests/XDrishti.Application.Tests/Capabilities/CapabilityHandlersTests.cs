#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Capabilities;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Capabilities;

/// <summary>Spec P8-18 Part A (ADR 0128) behind the ports: the gate reads the log (OFF without a row, OFF for an unknown name), the owner's switch writes one row and one audit event, and a repeated switch writes nothing. The log is in memory and behaves like the store.</summary>
public sealed class CapabilityHandlersTests
{
    private const string Owner = "owner:test@example.com";

    private static readonly DateTimeOffset At = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private readonly FakeLog _log = new();

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();

    private readonly IAccountUnitOfWork _unit = Substitute.For<IAccountUnitOfWork>();

    private CapabilityGate Gate() => new(_log);

    private ChangeCapabilityHandler Handler() => new(_log, new CapabilityViews(_log), _events, _unit, new FixedTime(At), NullLogger<ChangeCapabilityHandler>.Instance);

    private ValueTask<Result<CapabilityChangeOutcome>> Switch(string name, bool on, string actor = Owner, string reason = "I want it") =>
        Handler().HandleAsync(new ChangeCapabilityCommand(name, on, reason, actor), TestContext.Current.CancellationToken);

    [Fact]
    public async Task With_no_row_every_capability_is_off_and_the_gate_refuses_with_a_403()
    {
        foreach (var info in XDrishti.Domain.Capabilities.Capabilities.All)
        {
            var gate = await Gate().RequireAsync(info.Name, TestContext.Current.CancellationToken);

            gate.IsFailure.ShouldBeTrue(info.Name);
            gate.Error.Code.ShouldBe("capabilities.off");
            gate.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        }

        var view = await new GetCapabilitiesHandler(new CapabilityViews(_log)).HandleAsync(new GetCapabilitiesQuery(), TestContext.Current.CancellationToken);
        view.Items.ShouldAllBe(i => !i.Enabled && i.ChangedAt == null && i.Reason == null);
        view.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_name_outside_the_whitelist_is_off_even_when_a_row_exists_for_it()
    {
        _log.Rows.Add(new CapabilityEntry(1, "apply_changes", true, "x", Owner, At));

        (await Gate().IsEnabledAsync("apply_changes", TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await Gate().IsEnabledAsync("", TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await new GetCapabilitiesHandler(new CapabilityViews(_log)).HandleAsync(new GetCapabilitiesQuery(), TestContext.Current.CancellationToken)).History.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owner_switches_one_capability_on_and_only_that_one_the_gate_opens_and_an_audit_event_names_it_without_the_reason()
    {
        var result = await Switch("experiments_ui", true, reason: "secret-looking reason text");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Changed.ShouldBeTrue();
        result.Value.Capabilities.Items.Single(i => i.Name == "experiments_ui").Enabled.ShouldBeTrue();
        result.Value.Capabilities.Items.Where(i => i.Name != "experiments_ui").ShouldAllBe(i => !i.Enabled);
        (await Gate().RequireAsync("experiments_ui", TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        (await Gate().RequireAsync("exports", TestContext.Current.CancellationToken)).IsFailure.ShouldBeTrue();
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.CapabilityChanged && e.Detail == "name=experiments_ui on" && e.Actor == Owner));
        await _unit.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Switching_it_off_again_closes_the_gate_and_the_newest_row_decides()
    {
        await Switch("exports", true);
        var off = await Switch("exports", false);

        off.Value.Changed.ShouldBeTrue();
        (await Gate().IsEnabledAsync("exports", TestContext.Current.CancellationToken)).ShouldBeFalse();
        off.Value.Capabilities.History.Select(h => (h.Name, h.Enabled)).ShouldBe([("exports", false), ("exports", true)]);
    }

    [Fact]
    public async Task The_same_switch_again_writes_nothing_and_records_no_second_audit_event()
    {
        await Switch("pwa", true);

        var again = await Switch("pwa", true);
        var offFirst = await Switch("exports", false); // already off: nothing to switch

        again.Value.Changed.ShouldBeFalse();
        offFirst.Value.Changed.ShouldBeFalse();
        _log.Rows.Count.ShouldBe(1);
        _events.ReceivedWithAnyArgs(1).Record(default!);
    }

    [Theory]
    [InlineData("cli:xd")]
    [InlineData("job:experiment")]
    [InlineData("")]
    public async Task A_caller_that_is_not_the_signed_in_owner_cannot_switch_anything_and_nothing_is_written(string actor)
    {
        var result = await Switch("experiments_ui", true, actor);

        result.Error.Code.ShouldBe("capabilities.owner_only");
        _log.Rows.ShouldBeEmpty();
        _events.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [Fact]
    public async Task An_unknown_name_and_a_blank_reason_are_refused_before_the_log_is_touched()
    {
        (await Switch("apply_changes", true)).Error.Code.ShouldBe("capabilities.not_found");
        (await Switch("exports", true, reason: "  ")).Error.Code.ShouldBe("capabilities.invalid");

        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void The_gate_has_no_way_to_switch_and_the_read_port_has_none_either()
    {
        typeof(ICapabilityGate).GetMethods().Select(m => m.Name).ShouldBe(["RequireAsync", "IsEnabledAsync"], ignoreOrder: true);
        typeof(ICapabilityLog).GetMethods().Select(m => m.Name).ShouldBe(["NewestAsync", "HistoryAsync"], ignoreOrder: true);
        typeof(ICapabilityLogWriter).GetMethods().Select(m => m.Name).ShouldBe(["AppendAsync"]);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeLog : ICapabilityLog, ICapabilityLogWriter
    {
        public List<CapabilityEntry> Rows { get; } = [];

        public ValueTask<IReadOnlyList<CapabilityEntry>> NewestAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CapabilityEntry>>([.. Rows.GroupBy(r => r.Name).Select(g => g.OrderByDescending(r => r.Id).First())]);

        public ValueTask<IReadOnlyList<CapabilityEntry>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CapabilityEntry>>([.. Rows.OrderByDescending(r => r.Id).Take(limit)]);

        public ValueTask<Result<CapabilityAppend>> AppendAsync(CapabilityChange change, CancellationToken cancellationToken)
        {
            var newest = Rows.Where(r => r.Name == change.Name).OrderByDescending(r => r.Id).FirstOrDefault();
            if ((newest?.Enabled ?? false) == change.Enabled)
            {
                return ValueTask.FromResult(Result.Success(new CapabilityAppend(newest, Created: false)));
            }

            var row = new CapabilityEntry(Rows.Count + 1, change.Name, change.Enabled, change.Reason, change.Actor, At);
            Rows.Add(row);
            return ValueTask.FromResult(Result.Success(new CapabilityAppend(row, Created: true)));
        }
    }
}
